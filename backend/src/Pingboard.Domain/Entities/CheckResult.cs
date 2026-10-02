using Pingboard.Domain.Common;

namespace Pingboard.Domain.Entities;

/// <summary>
///     Результат одной HTTP-проверки монитора. Append-only: записи никогда не меняются.
/// </summary>
public sealed class CheckResult
{
    private CheckResult()
    {
    } // для материализации EF Core

    private CheckResult(Guid monitorId, DateTimeOffset checkedAt, ProbeOutcome outcome)
    {
        MonitorId = monitorId;
        CheckedAt = checkedAt;
        Ok = outcome.Ok;
        StatusCode = outcome.StatusCode;
        LatencyMs = outcome.LatencyMs;
        Error = outcome.Error;
    }

    /// <summary>bigint identity — история проверок растёт быстрее всего, Guid здесь не нужен.</summary>
    public long Id { get; private set; }

    public Guid MonitorId { get; private set; }

    public DateTimeOffset CheckedAt { get; private set; }

    public bool Ok { get; private set; }

    public int? StatusCode { get; private set; }

    public int? LatencyMs { get; private set; }

    public string? Error { get; private set; }

    /// <summary>Единственная фабрика: снаружи можно прийти только с результатом пробера.</summary>
    public static CheckResult FromProbe(Guid monitorId, DateTimeOffset checkedAt, ProbeOutcome outcome)
    {
        if (monitorId == Guid.Empty) throw DomainValidationException.For(nameof(monitorId), "не задан монитор");

        if (outcome.Ok && outcome.StatusCode is null)
            throw DomainValidationException.For(nameof(outcome), "успешная проверка обязана иметь статус-код");

        // Отрицательная задержка — это не «быстрый ответ», а битый замер (переполнение
        // счётчика, путаница единиц). Лучше уронить запись, чем показать её на дашборде.
        if (outcome.LatencyMs is < 0)
            throw DomainValidationException.For(nameof(outcome), "задержка не может быть отрицательной");

        return new CheckResult(monitorId, checkedAt, outcome);
    }
}
