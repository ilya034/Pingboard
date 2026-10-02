using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pingboard.Application.Abstractions;
using Pingboard.Application.Probing.Options;
using Pingboard.Domain.Common;
using Pingboard.Domain.Entities;

namespace Pingboard.Application.Probing;

/// <summary>
///     Сценарий воркера: найти «просроченные» мониторы, проверить их и записать результаты.
///     Воркер не знает ни про HttpClient, ни про EF Core — только про порты.
/// </summary>
public sealed class RunDueChecks(
    IMonitorRepository monitors,
    ICheckRepository checks,
    IProbeService probe,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    IOptions<DueCheckOptions> options,
    ILogger<RunDueChecks> logger)
{
    public async Task<int> ExecuteAsync(CancellationToken ct)
    {
        var cfg = options.Value;
        var now = timeProvider.GetUtcNow();

        var due = await monitors.ListDueAsync(now, cfg.BatchSize, ct);
        if (due.Count == 0) return 0;

        var parallelism = Math.Max(1, cfg.MaxParallel);
        var results = new ConcurrentBag<(Monitor Monitor, ProbeOutcome Outcome, DateTimeOffset CheckedAt)>();

        await Parallel.ForEachAsync(
            due,
            new ParallelOptions { MaxDegreeOfParallelism = parallelism, CancellationToken = ct },
            async (monitor, token) =>
            {
                ProbeOutcome outcome;
                DateTimeOffset checkedAt;

                try
                {
                    outcome = await probe.CheckAsync(monitor.Url, token);

                    // Метка — момент ЗАВЕРШЕНИЯ проверки, а не её начала: иначе при интервале
                    // в 10 с и таймауте в 5 с монитор проверялся бы «встык», без паузы,
                    // а последняя проверка в истории выглядела бы старше, чем она есть.
                    checkedAt = timeProvider.GetUtcNow();
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw; // остановка приложения — это не «неуспешная проверка»
                }
                catch (Exception ex)
                {
                    // Контракт IProbeService — «наружу не бросает» (см. файл порта), но цикл
                    // не имеет права на это полагаться: одно неожиданное исключение убило бы
                    // всю итерацию вместе с уже сделанными проверками, и батч вернулся бы в очередь.
                    logger.LogWarning(ex, "Проверка монитора {MonitorId} завершилась исключением", monitor.Id);

                    // Наружу (в историю проверок, которую видит владелец) — общая формулировка:
                    // текст исключения может содержать внутренние детали.
                    outcome = ProbeOutcome.Failure("Внутренняя ошибка проверки");
                    checkedAt = timeProvider.GetUtcNow();
                }

                results.Add((monitor, outcome, checkedAt));
            });

        var batch = new List<CheckResult>(results.Count);

        foreach (var (monitor, outcome, checkedAt) in results)
        {
            batch.Add(CheckResult.FromProbe(monitor.Id, checkedAt, outcome));
            monitor.RecordCheck(outcome, checkedAt);
        }

        // Одна транзакция на батч: либо записаны все результаты итерации, либо ни один.
        await checks.AddRangeAsync(batch, ct);
        await unitOfWork.SaveChangesAsync(ct);

        var failed = batch.Count(c => !c.Ok);
        logger.LogInformation(
            "Проверено мониторов: {Checked}, из них неуспешных: {Failed}, параллелизм: {Parallelism}",
            batch.Count,
            failed,
            parallelism);

        return batch.Count;
    }
}
