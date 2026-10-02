namespace Pingboard.Application.Monitors.Dtos;

public sealed record CreateMonitorRequest(string? Name, string? Url, int? IntervalSeconds, bool? Enabled);
