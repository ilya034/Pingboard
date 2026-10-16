using Pingboard.Domain.Common;

namespace Pingboard.Domain.Entities;

/// <summary>
///     Результат одной HTTP-проверки монитора. Append-only: записи никогда не меняются.
/// </summary>
public sealed class CheckResult
{
    private CheckResult()
    {
    } // EF Core

    private CheckResult(Guid monitorId, DateTimeOffset checkedAt, ProbeOutcome outcome)
    {
        MonitorId = monitorId;
        CheckedAt = checkedAt;
        Ok = outcome.Ok;
        StatusCode = outcome.StatusCode;
        LatencyMs = outcome.LatencyMs;
        Error = outcome.Error;
    }

    /// <summary>Уникальный идентификатор записи проверки.</summary>
    public long Id { get; private set; }

    public Guid MonitorId { get; private set; }

    public DateTimeOffset CheckedAt { get; private set; }

    public bool Ok { get; private set; }

    public int? StatusCode { get; private set; }

    public int? LatencyMs { get; private set; }

    public string? Error { get; private set; }

    /// <summary>
    ///     Создаёт экземпляр результата проверки на основе полученного ответа пробера.
    /// </summary>
    /// <param name="monitorId">Идентификатор опрашиваемого монитора.</param>
    /// <param name="checkedAt">Временная метка выполнения проверки.</param>
    /// <param name="outcome">Результат выполнения HTTP-пробы.</param>
    public static CheckResult FromProbe(Guid monitorId, DateTimeOffset checkedAt, ProbeOutcome outcome)
    {
        if (monitorId == Guid.Empty) throw DomainValidationException.For(nameof(monitorId), "monitor is required");

        if (outcome.Ok && outcome.StatusCode is null)
            throw DomainValidationException.For(nameof(outcome), "probe must have status code if it is ok");
        
        if (outcome.LatencyMs is < 0)
            throw DomainValidationException.For(nameof(outcome), "latency cannot be negative");

        return new CheckResult(monitorId, checkedAt, outcome);
    }
}
