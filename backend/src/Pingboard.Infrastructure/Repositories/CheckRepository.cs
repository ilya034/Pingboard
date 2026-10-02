using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pingboard.Application.Abstractions;
using Pingboard.Domain.Entities;
using Pingboard.Infrastructure.Persistence;

namespace Pingboard.Infrastructure.Repositories;

/// <summary>Запись и чтение истории проверок: только сущности наружу, без IQueryable.</summary>
public sealed class CheckRepository(UptimeDbContext db) : ICheckRepository
{
    public async Task AddRangeAsync(IEnumerable<CheckResult> checks, CancellationToken ct)
    {
        await db.CheckResults.AddRangeAsync(checks, ct);
    }

    public async Task<IReadOnlyList<CheckResult>> HistoryAsync(
        Guid monitorId,
        DateTimeOffset from,
        DateTimeOffset to,
        int limit,
        CancellationToken ct)
    {
        return await db.CheckResults
            .AsNoTracking()
            .Where(c => c.MonitorId == monitorId && c.CheckedAt >= from && c.CheckedAt <= to)
            .OrderByDescending(c => c.CheckedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    /// <summary>История сразу по всем мониторам дашборда: один запрос вместо N+1.</summary>
    public async Task<IReadOnlyList<CheckResult>> HistoryForManyAsync(
        IReadOnlyCollection<Guid> monitorIds,
        DateTimeOffset from,
        DateTimeOffset to,
        int limitPerMonitor,
        CancellationToken ct)
    {
        if (monitorIds.Count == 0 || limitPerMonitor <= 0) return [];

        return await HistoryForManyQuery(db, monitorIds.ToArray(), from, to, limitPerMonitor)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    /// <summary>
    ///     «Top N на монитор» одним запросом. LINQ для этого не годится: EF Core транслирует
    ///     GroupBy только с агрегатами (GroupBy + Take не поддерживается), а прежний глобальный
    ///     <c>Take(limit × N)</c> давал starvation — монитор с интервалом 10 с вытеснял из выборки
    ///     редкие, и те показывались на дашборде серыми, хотя данные есть. Поэтому явный ROW_NUMBER.
    ///     Метод internal static: текст SQL проверяется тестом без живой БД (ToQueryString).
    /// </summary>
    internal static IQueryable<CheckResult> HistoryForManyQuery(
        UptimeDbContext db,
        Guid[] monitorIds,
        DateTimeOffset from,
        DateTimeOffset to,
        int limitPerMonitor)
    {
        const string sql = $"""
            SELECT id, monitor_id, checked_at, ok, status_code, latency_ms, error
            FROM (
                SELECT c.*, row_number() OVER (PARTITION BY c.monitor_id ORDER BY c.checked_at DESC) AS rn
                FROM {NamingConventions.ChecksTable} AS c
                WHERE c.monitor_id = ANY(@monitor_ids)
                  AND c.checked_at >= @from
                  AND c.checked_at <= @to
            ) AS ranked
            WHERE ranked.rn <= @limit_per_monitor
            ORDER BY ranked.checked_at DESC
            """;

        return db.CheckResults.FromSqlRaw(
            sql,
            new NpgsqlParameter("monitor_ids", monitorIds),
            new NpgsqlParameter("from", from),
            new NpgsqlParameter("to", to),
            new NpgsqlParameter("limit_per_monitor", limitPerMonitor));
    }

    /// <summary>
    ///     uptime за окно: считается на лету (count FILTER в терминах SQL), кэш не нужен на таких объёмах.
    /// </summary>
    public async Task<double?> UptimeRatioAsync(Guid monitorId, DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct)
    {
        // Один агрегатный запрос, а не два раздельных COUNT: между ними успевала вставиться
        // проверка, и тогда ok мог оказаться больше total (uptime > 1 — уже неверный ответ).
        var aggregate = await UptimeRatiosQuery(db, [monitorId], from, to).FirstOrDefaultAsync(ct);

        return aggregate is null ? null : (double)aggregate.Ok / aggregate.Total;
    }

    /// <summary>uptime по всем мониторам дашборда одним агрегатом — вместо двух Count на каждый монитор.</summary>
    public async Task<IReadOnlyDictionary<Guid, double?>> UptimeRatioForManyAsync(
        IReadOnlyCollection<Guid> monitorIds,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct)
    {
        if (monitorIds.Count == 0) return new Dictionary<Guid, double?>();

        var aggregates = await UptimeRatiosQuery(db, monitorIds.ToArray(), from, to).ToListAsync(ct);

        return aggregates.ToDictionary(a => a.MonitorId, a => (double?)a.Ok / a.Total);
    }

    /// <summary>
    ///     GROUP BY monitor_id с подсчётом успешных: один запрос на весь список мониторов.
    ///     Мониторы без проверок в окне в результат не попадают (uptime неизвестен, а не ноль).
    ///     Метод internal static: SQL проверяется тестом без живой БД (ToQueryString).
    /// </summary>
    /// <summary>
    ///     GROUP BY monitor_id с подсчётом успешных: один запрос на весь список мониторов.
    ///     Мониторы без проверок в окне в результат не попадают (uptime неизвестен, а не ноль).
    ///     Метод internal static: SQL проверяется тестом без живой БД (ToQueryString).
    /// </summary>
    internal static IQueryable<CheckUptimeAggregate> UptimeRatiosQuery(
        UptimeDbContext db,
        Guid[] monitorIds,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        return db.CheckResults
            .AsNoTracking()
            .Where(c => monitorIds.Contains(c.MonitorId) && c.CheckedAt >= from && c.CheckedAt <= to)
            .GroupBy(c => c.MonitorId)
            .Select(g => new CheckUptimeAggregate(g.Key, g.Count(), g.Count(c => c.Ok)));
    }

    /// <summary>
    ///     Плотность проверок по сегментам окна — один GROUP BY на весь дашборд. Номер сегмента
    ///     считается арифметикой по времени самой проверки (epoch / длину сегмента), поэтому
    ///     полоса не зависит от того, сколько проверок успело попасть в память: монитор с
    ///     интервалом 10 с отдаёт за 24 ч 8640 проверок, а на дашборд уезжают 24 числа.
    ///     Метод internal static: текст SQL проверяется тестом без живой БД (ToQueryString).
    /// </summary>
    internal static IQueryable<CheckBucketAggregate> UptimeBucketsQuery(
        UptimeDbContext db,
        Guid[] monitorIds,
        DateTimeOffset from,
        DateTimeOffset to,
        int segments)
    {
        var segmentSeconds = (to - from).TotalSeconds / segments;

        return db.CheckResults
            .AsNoTracking()
            .Where(c => monitorIds.Contains(c.MonitorId) && c.CheckedAt >= from && c.CheckedAt <= to)
            .GroupBy(c => new
            {
                c.MonitorId,
                Index = (int)((c.CheckedAt - from).TotalSeconds / segmentSeconds)
            })
            .Select(g => new CheckBucketAggregate(
                g.Key.MonitorId,
                g.Key.Index,
                g.Count(),
                g.Count(c => !c.Ok)));
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<CheckBucket>>> UptimeBucketsForManyAsync(
        IReadOnlyCollection<Guid> monitorIds,
        DateTimeOffset from,
        DateTimeOffset to,
        int segments,
        CancellationToken ct)
    {
        if (monitorIds.Count == 0 || segments <= 0 || to <= from)
            return new Dictionary<Guid, IReadOnlyList<CheckBucket>>();

        var rows = await UptimeBucketsQuery(db, monitorIds.ToArray(), from, to, segments).ToListAsync(ct);

        return rows
            .GroupBy(r => r.MonitorId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<CheckBucket>)g
                    .Select(r => new CheckBucket(r.Index, r.Total, r.Failed))
                    .OrderBy(b => b.Index)
                    .ToArray());
    }
}
