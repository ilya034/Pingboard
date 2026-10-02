namespace Pingboard.Application.Monitors.Dtos;

/// <summary>Сегмент полосы uptime (по умолчанию — час из последних 24).</summary>
public sealed record UptimeBucketDto(DateTimeOffset From, DateTimeOffset To, int Total, int Failed)
{
    /// <summary>unknown — проверок в сегменте не было; иначе up/down.</summary>
    public string State => Total == 0 ? "unknown" : Failed == 0 ? "up" : "down";
}
