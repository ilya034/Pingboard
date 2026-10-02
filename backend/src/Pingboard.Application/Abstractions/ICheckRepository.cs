using Pingboard.Domain.Entities;

namespace Pingboard.Application.Abstractions;

public interface ICheckRepository
{
    Task AddRangeAsync(IEnumerable<CheckResult> checks, CancellationToken ct);

    Task<IReadOnlyList<CheckResult>> HistoryAsync(Guid monitorId, DateTimeOffset from, DateTimeOffset to, int limit,
        CancellationToken ct);

    /// <summary>
    ///     История сразу по нескольким мониторам — чтобы дашборд не делал N+1 запросов.
    ///     Лимит соблюдается <b>на каждый монитор</b>: иначе монитор с частым интервалом
    ///     вытесняет из выборки редкие, и те выглядят «серыми» без данных.
    /// </summary>
    Task<IReadOnlyList<CheckResult>> HistoryForManyAsync(IReadOnlyCollection<Guid> monitorIds, DateTimeOffset from,
        DateTimeOffset to, int limitPerMonitor, CancellationToken ct);

    /// <summary>0..1 или null, если данных за окно нет.</summary>
    Task<double?> UptimeRatioAsync(Guid monitorId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    /// <summary>
    ///     uptime сразу по нескольким мониторам: один агрегирующий запрос (<c>GROUP BY monitor_id</c>)
    ///     вместо двух <c>Count</c> на каждый монитор. Мониторы без проверок в окне в словарь не попадают.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, double?>> UptimeRatioForManyAsync(IReadOnlyCollection<Guid> monitorIds,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    /// <summary>
    ///     Плотность проверок по сегментам окна: сколько всего и сколько неуспешных в каждом
    ///     сегменте. Полоса доступности строится отсюда, а не из <see cref="HistoryForManyAsync" />:
    ///     та ограничена глубиной истории на монитор, и на мониторе с интервалом 10 с 24-часовое
    ///     окно (8640 проверок) закрывалось лишь последним часом, то есть 20 из 24 сегментов
    ///     показывались серыми, хотя uptime24h считался по всему окну.
    ///     Мониторы без проверок в окне в словарь не попадают: их полоса — все сегменты unknown.
    ///     Реализация по умолчанию пуста и это осознанный контракт: сценарии дашборда умеют
    ///     строить полосу из истории, поэтому метод необязателен для реализации.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<CheckBucket>>> UptimeBucketsForManyAsync(
        IReadOnlyCollection<Guid> monitorIds, DateTimeOffset from, DateTimeOffset to, int segments,
        CancellationToken ct)
    {
        return Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<CheckBucket>>>(
            new Dictionary<Guid, IReadOnlyList<CheckBucket>>());
    }
}
