using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pingboard.Application.Abstractions;
using Pingboard.Domain.Entities;
using Pingboard.Infrastructure.Persistence;

namespace Pingboard.Infrastructure.Repositories;

/// <summary>
/// Репозиторий для сохранения и чтения результатов проверок доступности.
/// Предоставляет методы выборки истории и агрегатов без утечки IQueryable за пределы слоя инфраструктуры.
/// </summary>
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

    /// <summary>
    /// Возвращает историю последних проверок для нескольких мониторов за указанный временной интервал.
    /// Выполняется одним запросом с ограничением количества записей на монитор.
    /// </summary>
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
    /// Формирует SQL-запрос для выборки последних N проверок на каждый монитор с использованием оконной функции ROW_NUMBER.
    /// Исключает starvation редких проверок при глобальном LIMIT и поддерживается тестами без подключения к БД.
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
    /// Рассчитывает долю успешных проверок (uptime ratio) для указанного монитора за период.
    /// </summary>
    public async Task<double?> UptimeRatioAsync(Guid monitorId, DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct)
    {
        // Выполняется единым агрегатным запросом, чтобы избежать расхождения между успешными и общими проверками.
        var aggregate = await UptimeRatiosQuery(db, [monitorId], from, to).FirstOrDefaultAsync(ct);

        return aggregate is null ? null : (double)aggregate.Ok / aggregate.Total;
    }

    /// <summary>
    /// Рассчитывает долю успешных проверок для нескольких мониторов одним агрегатным запросом.
    /// </summary>
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
    /// Формирует запрос группировки проверок по мониторам с подсчётом общего числа и успешных проверок.
    /// Мониторы без проверок в окне не включаются в выборку.
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
