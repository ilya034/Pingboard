using Pingboard.Application.Monitors.Dtos;

namespace Pingboard.Application.Monitors;

/// <summary>Сводка по монитору для списка/деталей: uptime, полоса, последняя задержка.</summary>
public sealed record MonitorStatusSummary(
    double? UptimeRatio,
    int? LastLatencyMs,
    IReadOnlyList<UptimeBucketDto> UptimeBar);
