using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pingboard.Application.Abstractions;
using Pingboard.Application.Observability;
using Pingboard.Application.Probing;
using Pingboard.Application.Probing.Options;
using Pingboard.Worker.Hosting;
using Pingboard.Worker.Options;

namespace Pingboard.Worker;

/// <summary>
/// Фоновый сервис периодического выполнения проверок доступности мониторов.
/// Управляет жизненным циклом итераций, изоляцией контекстов в рамках scope и записью файла пульса (heartbeat).
/// </summary>
public sealed class DueCheckWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<WorkerOptions> workerOptions,
    IOptions<DueCheckOptions> checkOptions,
    ILogger<DueCheckWorker> logger,
    IPingboardMetrics? metrics = null,
    WorkerChaosState? chaosState = null) : BackgroundService
{
    /// <summary>О недоступном пульсе предупреждаем один раз, а не каждые 5 секунд.</summary>
    private bool _heartbeatUnavailableLogged;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tick = TimeSpan.FromSeconds(workerOptions.Value.TickSeconds);
        var telemetry = metrics ?? PingboardMetrics.Default;

        // Логируем ровно те значения, которыми пользуется сценарий: батч и параллелизм
        // берутся из DueCheckOptions, а не из второго класса-двойника с теми же полями.
        logger.LogInformation("Worker started: tick {TickSeconds} s, batch {BatchSize}, parallelism {MaxParallel}",
            workerOptions.Value.TickSeconds, checkOptions.Value.BatchSize, checkOptions.Value.MaxParallel);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var failureRate = chaosState?.FailureRate ?? 0.0;
                if (failureRate > 0.0 && Random.Shared.NextDouble() < failureRate)
                    throw new InvalidOperationException("Simulated worker iteration failure (chaos simulation).");

                // DbContext scoped на итерацию: никаких долгоживущих контекстов.
                await using var scope = scopeFactory.CreateAsyncScope();
                var scenario = scope.ServiceProvider.GetRequiredService<RunDueChecks>();
                await scenario.ExecuteAsync(stoppingToken);
                telemetry.RecordWorkerIteration(success: true);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // штатное завершение по SIGTERM
            }
            catch (Exception ex)
            {
                telemetry.RecordWorkerIteration(success: false);
                // Ошибка отдельной итерации не прерывает работу цикла фоновой проверки.
                logger.LogWarning(ex, "Check iteration failed");
            }

            // Пульс обновляется и после неудачной итерации, подтверждая активность фонового процесса.
            WriteHeartbeat(workerOptions.Value.HeartbeatPath);

            try
            {
                await Task.Delay(tick, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("Worker stopped");
    }

    /// <summary>
    /// Записывает отметку времени завершения итерации в файл пульса для внешней проверки работоспособности.
    /// Сбои файловой системы логируются, но не приводят к падению сервиса.
    /// </summary>
    private void WriteHeartbeat(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            File.WriteAllText(path, DateTimeOffset.UtcNow.ToString("O"));
        }
        catch (Exception ex)
        {
            if (_heartbeatUnavailableLogged) return;

            _heartbeatUnavailableLogged = true;
            logger.LogWarning(ex, "Failed to write heartbeat file {Path}: worker healthcheck will not work", path);
        }
    }
}
