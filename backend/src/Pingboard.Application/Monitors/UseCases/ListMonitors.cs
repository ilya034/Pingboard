using Microsoft.Extensions.Options;
using Pingboard.Application.Abstractions;
using Pingboard.Application.Monitors.Dtos;
using Pingboard.Application.Monitors.Options;
using Pingboard.Domain.Entities;

namespace Pingboard.Application.Monitors.UseCases;

/// <summary>Список мониторов владельца с uptime и полосой доступности за окно.</summary>
public sealed class ListMonitors(
    IMonitorRepository monitors,
    ICheckRepository checks,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    IOptions<MonitorsOptions> options)
{
    public async Task<IReadOnlyList<MonitorDto>> ExecuteAsync(CancellationToken ct)
    {
        var cfg = options.Value;
        var now = timeProvider.GetUtcNow();
        var from = now - TimeSpan.FromHours(cfg.UptimeWindowHours);

        var owned = await monitors.ListAsync(currentUser.UserId, ct);
        if (owned.Count == 0) return [];

        var ids = owned.Select(m => m.Id).ToArray();

        // Три запроса на весь дашборд независимо от числа мониторов: история, агрегат uptime
        // и плотность по сегментам полосы. Раньше здесь было 2N+1 обращений (UptimeRatioAsync
        // в цикле делает два CountAsync).
        var history = await checks.HistoryForManyAsync(ids, from, now, cfg.DashboardHistoryPerMonitor, ct);
        var uptimes = await checks.UptimeRatioForManyAsync(ids, from, now, ct);
        var buckets = await checks.UptimeBucketsForManyAsync(ids, from, now, cfg.UptimeBarSegments, ct);

        var byMonitor = history
            .GroupBy(c => c.MonitorId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<CheckResult>)g.OrderBy(c => c.CheckedAt).ToArray());

        var result = new List<MonitorDto>(owned.Count);

        foreach (var monitor in owned)
        {
            var uptime = uptimes.TryGetValue(monitor.Id, out var ratio) ? ratio : null;
            var monitorChecks = byMonitor.TryGetValue(monitor.Id, out var list) ? list : [];

            // Полоса — из агрегата: история ограничена глубиной на монитор и на частых
            // мониторах закрывает лишь хвост окна. Пустой словарь (репозиторий без поддержки
            // агрегата) даёт тот же результат через историю, когда проверок в окне нет.
            var bar = buckets.TryGetValue(monitor.Id, out var perSegment)
                ? MonitorMapper.BuildUptimeBar(perSegment, from, now, cfg.UptimeBarSegments)
                : MonitorMapper.BuildUptimeBar(monitorChecks, from, now, cfg.UptimeBarSegments);

            result.Add(MonitorMapper.ToDto(monitor, new MonitorStatusSummary(
                uptime,
                // Максимум по времени, а не «последний в списке»: порядок задаёт репозиторий.
                monitorChecks.MaxBy(c => c.CheckedAt)?.LatencyMs,
                bar)));
        }

        return result;
    }
}
