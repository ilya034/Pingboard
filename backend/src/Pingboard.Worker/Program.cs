using Microsoft.Extensions.DependencyInjection;
using Pingboard.Application;
using Pingboard.Infrastructure;
using Pingboard.Worker;
using Pingboard.Worker.Hosting;
using Pingboard.Worker.Options;

// Админ-режим той же сборки: «проверить пульс и выйти». Его вызывает docker healthcheck
// и livenessProbe в k8s — вместо shell-однострочника с date/stat, который зависит от состава
// образа (фактор IX). Код возврата: 0 — пульс свежий, 1 — пульс устарел или отсутствует.
//
// Environment.Exit, а не `return`: точка входа с top-level statements требует, чтобы значение
// возвращали ВСЕ пути, а обычный запуск воркера ничего не возвращает — цикл живёт до SIGTERM.
var selfCheckExitCode = WorkerSelfCheck.Run(args, TimeProvider.System.GetUtcNow());
if (selfCheckExitCode is not null) Environment.Exit(selfCheckExitCode.Value);

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
