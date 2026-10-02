using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pingboard.Application.Probing;
using Pingboard.Application.Probing.Options;
using Pingboard.Worker.Options;

namespace Pingboard.Worker;

/// <summary>
///     Цикл пингов. Вся «логика планировщика» — в сценарии RunDueChecks, здесь только:
///     scope на итерацию, защита цикла от падения и пауза.
/// </summary>
public sealed class DueCheckWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<WorkerOptions> workerOptions,
    IOptions<DueCheckOptions> checkOptions,
    ILogger<DueCheckWorker> logger) : BackgroundService
{
    /// <summary>О недоступном пульсе предупреждаем один раз, а не каждые 5 секунд.</summary>
    private bool _heartbeatUnavailableLogged;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tick = TimeSpan.FromSeconds(workerOptions.Value.TickSeconds);

        // Логируем ровно те значения, которыми пользуется сценарий: батч и параллелизм
        // берутся из DueCheckOptions, а не из второго класса-двойника с теми же полями.
        logger.LogInformation("Worker started: tick {TickSeconds} s, batch {BatchSize}, parallelism {MaxParallel}",
            workerOptions.Value.TickSeconds, checkOptions.Value.BatchSize, checkOptions.Value.MaxParallel);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // DbContext scoped на итерацию: никаких долгоживущих контекстов.
                await using var scope = scopeFactory.CreateAsyncScope();
                var scenario = scope.ServiceProvider.GetRequiredService<RunDueChecks>();
                await scenario.ExecuteAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // штатное завершение по SIGTERM
            }
            catch (Exception ex)
            {
                // Тик упал — цикл живёт дальше: один плохой монитор не роняет мониторинг (фактор IX).
                logger.LogWarning(ex, "Check iteration failed");
            }

            // Пульс обновляем и после неудачной итерации: цикл жив, а факт «БД недоступна»
            // виден по логам и /readyz — healthcheck не должен путать одно с другим.
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
    ///     Отметка «итерация доиграна». Ошибка записи (read-only ФС, чужой каталог) — не повод
    ///     ронять цикл проверок: без пульса просто не сработает healthcheck воркера.
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
