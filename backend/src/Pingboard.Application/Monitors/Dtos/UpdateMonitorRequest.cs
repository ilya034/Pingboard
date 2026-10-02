namespace Pingboard.Application.Monitors.Dtos;

/// <summary>PATCH: null означает «не менять».</summary>
public sealed record UpdateMonitorRequest(string? Name, string? Url, int? IntervalSeconds, bool? Enabled);
