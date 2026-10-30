using Microsoft.Extensions.DependencyInjection;
using Pingboard.Application;
using Pingboard.Infrastructure;
using Pingboard.Worker;
using Pingboard.Worker.Hosting;
using Pingboard.Worker.Options;

// Режим проверки состояния воркера (self-check для docker healthcheck / k8s probe):
// проверяет актуальность файла пульса. Возвращает 0 при успехе и 1 при устаревании или отсутствии файла.
var selfCheckExitCode = WorkerSelfCheck.Run(args, TimeProvider.System.GetUtcNow());
if (selfCheckExitCode is not null) Environment.Exit(selfCheckExitCode.Value);

await using var host = WorkerHost.Build(
    args,
    (services, configuration) =>
    {
        // Регистрация сервисов приложения и инфраструктуры.
        services.AddApplication();
        services.AddInfrastructure(configuration);

        // Период тика — конфигурация, а не константа в коде.
        services.AddOptions<WorkerOptions>()
            .Bind(configuration.GetSection(WorkerOptions.SectionName))
            .Validate(o => o.TickSeconds > 0, "Worker__TickSeconds must be greater than zero")
            .ValidateOnStart();

        services.AddSingleton<WorkerChaosState>();
        services.AddSingleton<WorkerMetricsServer>();
        services.AddSingleton<DueCheckWorker>();
    },
    typeof(WorkerMetricsServer),
    typeof(DueCheckWorker));

await host.RunAsync();
