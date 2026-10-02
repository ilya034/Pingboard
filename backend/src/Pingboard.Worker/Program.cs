using Microsoft.Extensions.DependencyInjection;
using Pingboard.Application;
using Pingboard.Infrastructure;
using Pingboard.Worker;
using Pingboard.Worker.Hosting;
using Pingboard.Worker.Options;

await using var host = WorkerHost.Build(
    args,
    (services, configuration) =>
    {
        // Тот же DI-корень, что и в Api: другой process type, тот же код (фактор VIII).
        services.AddApplication();
        services.AddInfrastructure(configuration);

        // Период тика — конфигурация, а не константа в коде.
        services.AddOptions<WorkerOptions>()
            .Bind(configuration.GetSection(WorkerOptions.SectionName))
            .Validate(o => o.TickSeconds > 0, "Worker__TickSeconds must be greater than zero")
            .ValidateOnStart();

        services.AddSingleton<DueCheckWorker>();
    },
    typeof(DueCheckWorker));

await host.RunAsync();
