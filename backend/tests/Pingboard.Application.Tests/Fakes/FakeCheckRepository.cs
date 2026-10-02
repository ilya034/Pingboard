using Pingboard.Application.Abstractions;
using Pingboard.Domain.Entities;

namespace Pingboard.Application.Tests.Fakes;

/// <summary>
///     Фейк истории проверок: повторяет контракт ICheckRepository без БД.
///     Лимиты считаются «на монитор» — ровно как в SQL-версии (ROW_NUMBER по группам),
///     иначе тесты дашборда проходили бы на другом поведении, чем в проде.
/// </summary>
public sealed class FakeCheckRepository : ICheckRepository
{
    private readonly List<CheckResult> _checks = [];

    public IReadOnlyList<CheckResult> All => _checks;

    /// <summary>
    ///     Сколько раз сценарий сходил за uptime одного монитора. Дашборд обязан
    ///     пользоваться пакетным запросом, иначе получается 2N+1 обращений к БД.
    /// </summary>
    public int SingleUptimeCalls { get; private set; }

    public Task AddRangeAsync(IEnumerable<CheckResult> checks, CancellationToken ct)
    {
        _checks.AddRange(checks);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CheckResult>> HistoryAsync(
        Guid monitorId,
        DateTimeOffset from,
        DateTimeOffset to,
        int limit,
        CancellationToken ct)
    {
        return Task.FromResult<IReadOnlyList<CheckResult>>(
            _checks
                .Where(c => c.MonitorId == monitorId && c.CheckedAt >= from && c.CheckedAt <= to)
                .OrderByDescending(c => c.CheckedAt)
                .Take(limit)
                .ToArray());
    }

    public Task<IReadOnlyList<CheckResult>> HistoryForManyAsync(
        IReadOnlyCollection<Guid> monitorIds,
        DateTimeOffset from,
        DateTimeOffset to,
        int limitPerMonitor,
        CancellationToken ct)
    {
        var window = _checks
            .Where(c => monitorIds.Contains(c.MonitorId) && c.CheckedAt >= from && c.CheckedAt <= to);

        return Task.FromResult<IReadOnlyList<CheckResult>>(
            window
                .GroupBy(c => c.MonitorId)
                .SelectMany(g => g.OrderByDescending(c => c.CheckedAt).Take(limitPerMonitor))
                .OrderByDescending(c => c.CheckedAt)
                .ToArray());
    }

    public Task<double?> UptimeRatioAsync(Guid monitorId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        SingleUptimeCalls++;

        var window = _checks
            .Where(c => c.MonitorId == monitorId && c.CheckedAt >= from && c.CheckedAt <= to)
            .ToArray();

        var ratio = window.Length == 0
            ? (double?)null
            : (double)window.Count(c => c.Ok) / window.Length;

        return Task.FromResult(ratio);
    }

    public Task<IReadOnlyDictionary<Guid, double?>> UptimeRatioForManyAsync(
        IReadOnlyCollection<Guid> monitorIds,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct)
    {
        var result = _checks
            .Where(c => monitorIds.Contains(c.MonitorId) && c.CheckedAt >= from && c.CheckedAt <= to)
            .GroupBy(c => c.MonitorId)
            .ToDictionary(g => g.Key, g => (double?)g.Count(c => c.Ok) / g.Count());

        return Task.FromResult<IReadOnlyDictionary<Guid, double?>>(result);
    }

    /// <summary>Удобно для тестов дашборда: положить проверки напрямую.</summary>
    public void Seed(IEnumerable<CheckResult> checks)
    {
        _checks.AddRange(checks);
    }
}
