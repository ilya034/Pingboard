namespace Pingboard.Application.Monitors.Dtos;

/// <summary>Внешнее представление монитора. Наружу отдаём только DTO, никогда — доменную сущность.</summary>
public sealed record MonitorDto(
    Guid Id,
    string Name,
    string Url,
    int IntervalSeconds,
    bool Enabled,
    bool? LastOk,
    DateTimeOffset? LastCheckedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    double? Uptime24h,
    int? LastLatencyMs,
    IReadOnlyList<UptimeBucketDto> UptimeBar);
