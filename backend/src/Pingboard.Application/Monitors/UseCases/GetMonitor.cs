using Microsoft.Extensions.Options;
using Pingboard.Application.Abstractions;
using Pingboard.Application.Monitors.Dtos;
using Pingboard.Application.Monitors.Options;

namespace Pingboard.Application.Monitors.UseCases;

/// <summary>Детали одного монитора: uptime, полоса, последняя задержка.</summary>
public sealed class GetMonitor(
    IMonitorRepository monitors,
    ICheckRepository checks,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    IOptions<MonitorsOptions> options)
{
    public async Task<MonitorDto> ExecuteAsync(Guid id, CancellationToken ct)
    {
        var cfg = options.Value;
        var now = timeProvider.GetUtcNow();
        var from = now - TimeSpan.FromHours(cfg.UptimeWindowHours);

        var monitor = await OwnedMonitor.LoadAsync(monitors, id, currentUser, ct);
        var history = await checks.HistoryAsync(id, from, now, cfg.DashboardHistoryPerMonitor, ct);
        var buckets = await checks.UptimeBucketsForManyAsync([id], from, now, cfg.UptimeBarSegments, ct);

        // Последняя задержка берётся максимумом по времени, а не history[0]: порядок элементов
        // в контракте порта не зафиксирован, и «первый = самый свежий» — негласное допущение,
        // которое молча ломается при смене сортировки в репозитории.
        var latestLatency = history.MaxBy(c => c.CheckedAt)?.LatencyMs;

        // Полоса — из агрегата по сегментам (история ограничена глубиной, см. ListMonitors);
        // пустой словарь — фолбэк на историю для репозиториев без поддержки агрегата.
        var bar = buckets.TryGetValue(monitor.Id, out var perSegment)
            ? MonitorMapper.BuildUptimeBar(perSegment, from, now, cfg.UptimeBarSegments)
            : MonitorMapper.BuildUptimeBar(history, from, now, cfg.UptimeBarSegments);

        return MonitorMapper.ToDto(monitor, new MonitorStatusSummary(
            await checks.UptimeRatioAsync(id, from, now, ct),
            latestLatency,
            bar));
    }
}
