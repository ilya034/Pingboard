using System.Runtime.InteropServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pingboard.Worker.Hosting.Configuration;
using Pingboard.Worker.Hosting.Logging;

namespace Pingboard.Worker.Hosting;

/// <summary>
/// Легковесный хост для запуска и управления жизненным циклом фоновых служб (IHostedService).
/// Выполняет инициализацию конфигурации, DI-контейнера, логирования и обработку сигналов завершения процесса.
/// </summary>
public sealed class WorkerHost : IAsyncDisposable
{
    /// <summary>
    /// Предельное время ожидания завершения фоновых служб при получении сигнала остановки (graceful shutdown).
    /// </summary>
    public static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(10);

    private readonly IReadOnlyList<IHostedService> _hostedServices;
    private readonly ILogger<WorkerHost> _logger;
    private readonly ServiceProvider _provider;
    private readonly CancellationTokenSource _stopping = new();

    /// <summary>Имя окружения, по которому выбран appsettings.{env}.json (для лога старта).</summary>
    private readonly string _environmentName;

    private WorkerHost(ServiceProvider provider, IReadOnlyList<IHostedService> hostedServices,
        ILogger<WorkerHost> logger, string environmentName)
    {
        _provider = provider;
        _hostedServices = hostedServices;
        _logger = logger;
        _environmentName = environmentName;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_stopping.IsCancellationRequested) _stopping.Cancel();

        _stopping.Dispose();
        await _provider.DisposeAsync();
    }

    public static WorkerHost Build(string[] args, Action<IServiceCollection, IConfiguration> configure,
        params Type[] hostedServiceTypes)
    {
        _ = args;
        var environmentName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                              ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                              ?? "Production";

        var basePath = AppContext.BaseDirectory;
        var configuration = new ConfigurationBuilder()
            .AddJsonFileIfExists(Path.Combine(basePath, "appsettings.json"))
            .AddJsonFileIfExists(Path.Combine(basePath, $"appsettings.{environmentName}.json"))
            .AddProcessEnvironmentVariables()
            .Build();

        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(TimeProvider.System);

        services.AddLogging(logging =>
        {
            logging.ClearProviders();
            ApplyLogLevels(logging, configuration);
            logging.AddProvider(new JsonConsoleLoggerProvider());
        });

        configure(services, configuration);

        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        // Штатный Host выполняет IStartupValidator сам (Host.StartAsync), а этот мини-хост — нет:
        // без явного вызова все ValidateOnStart() из AddInfrastructure/AddApplication и из
        // Worker/Program.cs инертны. Цена ошибки видна сразу: Worker__TickSeconds=0 не уронил бы
        // старт, а Task.Delay(0) превратил бы цикл проверок в busy-loop по БД и CPU.
        provider.GetService<IStartupValidator>()?.Validate();

        var hosted = hostedServiceTypes
            .Select(type => (IHostedService)provider.GetRequiredService(type))
            .ToArray();

        return new WorkerHost(provider, hosted, provider.GetRequiredService<ILogger<WorkerHost>>(),
            environmentName);
    }

    public async Task RunAsync()
    {
        using var signals = RegisterShutdownSignals();

        _logger.LogInformation(
            "Worker started (environment {Environment}); hosted services: {Count}",
            _environmentName,
            _hostedServices.Count);

        foreach (var service in _hostedServices) await service.StartAsync(_stopping.Token);

        try
        {
            await Task.Delay(Timeout.Infinite, _stopping.Token);
        }
        catch (OperationCanceledException)
        {
            // штатная остановка по сигналу
        }

        // Один бюджет на все сервисы: SIGTERM обязан уложиться в ShutdownTimeout, иначе
        // оркестратор пришлёт SIGKILL. Доигрывание CurrentIteration укладывается в него,
        // потому что цикл слушает stoppingToken.
        using var stopBudget = new CancellationTokenSource(ShutdownTimeout);

        foreach (var service in _hostedServices)
            try
            {
                await service.StopAsync(stopBudget.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning(
                    "Stopping {Service} exceeded {Timeout} s: forcing shutdown",
                    service.GetType().Name,
                    ShutdownTimeout.TotalSeconds);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Stopping {Service} failed", service.GetType().Name);
            }

        if (stopBudget.IsCancellationRequested)
            _logger.LogWarning("Shutdown budget ({Timeout} s) exhausted", ShutdownTimeout.TotalSeconds);

        _logger.LogInformation("Worker stopped cleanly");
    }

    /// <summary>
    /// Применяет настройки уровней логирования из секции Logging:LogLevel (Default и отдельные категории).
    /// </summary>
    private static void ApplyLogLevels(ILoggingBuilder logging, IConfiguration configuration)
    {
        var levels = configuration.GetSection("Logging:LogLevel");

        logging.SetMinimumLevel(ParseLevel(levels["Default"], LogLevel.Information));

        foreach (var category in levels.GetChildren())
        {
            if (category.Key == "Default" || category.Value is null) continue;

            if (Enum.TryParse<LogLevel>(category.Value, ignoreCase: true, out var level))
                logging.AddFilter(category.Key, level);
        }
    }

    private static LogLevel ParseLevel(string? value, LogLevel fallback) =>
        Enum.TryParse<LogLevel>(value, ignoreCase: true, out var level) ? level : fallback;

    private IDisposable RegisterShutdownSignals()
    {
        var sigterm = PosixSignalRegistration.Create(
            PosixSignal.SIGTERM,
            context =>
            {
                context.Cancel = true;
                _logger.LogInformation("Received SIGTERM: completing the current iteration");
                _stopping.Cancel();
            });

        Console.CancelKeyPress += OnCancelKeyPress;

        return new ActionDisposable(() =>
        {
            Console.CancelKeyPress -= OnCancelKeyPress;
            sigterm.Dispose();
        });
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs args)
    {
        args.Cancel = true;
        _logger.LogInformation("Received Ctrl+C: shutting down");
        _stopping.Cancel();
    }
}
