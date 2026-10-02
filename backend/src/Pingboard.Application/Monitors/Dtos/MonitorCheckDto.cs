namespace Pingboard.Application.Monitors.Dtos;

public sealed record MonitorCheckDto(
    long Id,
    DateTimeOffset CheckedAt,
    bool Ok,
    int? StatusCode,
    int? LatencyMs,
    string? Error);
