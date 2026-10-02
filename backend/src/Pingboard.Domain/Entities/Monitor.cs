using Pingboard.Domain.Common;

namespace Pingboard.Domain.Entities;

/// <summary>
///     Монитор - то, что пользователь поставил «на наблюдение».
///     Все переходы состояния идут через фабрику <see cref="Create" /> и методы
///     <see cref="ApplyUpdate" /> / <see cref="RecordCheck" /> / <see cref="SetEnabled" />:
///     статусные поля нельзя испортить снаружи.
/// </summary>
public sealed class Monitor : Entity<Guid>
{
    private Monitor()
    {
    } // EF Core

    private Monitor(Guid id, Guid ownerId, string name, string url, int intervalSeconds, bool enabled,
        DateTimeOffset now)
    {
        Id = id;
        OwnerId = ownerId;
        Name = name;
        Url = url;
        IntervalSeconds = intervalSeconds;
        Enabled = enabled;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid OwnerId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Url { get; private set; } = string.Empty;

    public int IntervalSeconds { get; private set; }

    public bool Enabled { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Денормализованное состояние воркера: живёт в БД, не в памяти процесса (фактор VI).</summary>
    public DateTimeOffset? LastCheckedAt { get; private set; }

    public bool? LastOk { get; private set; }

    /// <summary>Когда по расписанию положена следующая проверка. Производное значение, не хранится.</summary>
    public DateTimeOffset NextCheckDueAt =>
        (LastCheckedAt ?? DateTimeOffset.MinValue) + TimeSpan.FromSeconds(IntervalSeconds);

    public bool IsDue(DateTimeOffset now)
    {
        return Enabled && now >= NextCheckDueAt;
    }

    public static Monitor Create(Guid ownerId, string name, string url, int intervalSeconds, DateTimeOffset now,
        bool enabled = true)
    {
        var cleanName = ValidateName(name);
        var cleanUrl = ValidateUrl(url);
        var interval = ValidateInterval(intervalSeconds);

        if (ownerId == Guid.Empty) throw DomainValidationException.For(nameof(ownerId), "владелец не задан");

        return new Monitor(Guid.NewGuid(), ownerId, cleanName, cleanUrl, interval, enabled, now);
    }

    /// <summary>
    ///     Частичное обновление: применяем только явно переданные поля,
    ///     проверяя их ДО первой мутации, чтобы не оставить сущность в половинчатом состоянии.
    /// </summary>
    public void ApplyUpdate(string? name, string? url, int? intervalSeconds, bool? enabled, DateTimeOffset now)
    {
        var newName = name is null ? Name : ValidateName(name);
        var newUrl = url is null ? Url : ValidateUrl(url);
        var newInterval = intervalSeconds is null ? IntervalSeconds : ValidateInterval(intervalSeconds.Value);
        var newEnabled = enabled ?? Enabled;

        var changed = newName != Name || newUrl != Url || newInterval != IntervalSeconds || newEnabled != Enabled;

        Name = newName;
        Url = newUrl;
        IntervalSeconds = newInterval;
        Enabled = newEnabled;

        if (changed) UpdatedAt = now;
    }

    public void SetEnabled(bool enabled, DateTimeOffset now)
    {
        if (Enabled == enabled) return;

        Enabled = enabled;
        UpdatedAt = now;
    }

    /// <summary>
    ///     Единственный способ изменить статусные поля. Вызывается после записи <see cref="CheckResult" />.
    /// </summary>
    public void RecordCheck(ProbeOutcome outcome, DateTimeOffset checkedAt)
    {
        LastCheckedAt = checkedAt;
        LastOk = outcome.Ok;
    }

    private static string ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw DomainValidationException.For(nameof(Name), "name is required");

        var trimmed = name.Trim();
        if (trimmed.Length > MonitorRules.NameMaxLength)
            throw DomainValidationException.For(nameof(Name), $"not longer than {MonitorRules.NameMaxLength} characters");

        return trimmed;
    }

    private static string ValidateUrl(string? url)
    {
        if (!MonitorRules.IsSupportedUrl(url))
            throw DomainValidationException.For(nameof(Url), "absolute URL with http or https scheme is required");

        return url!.Trim();
    }

    private static int ValidateInterval(int intervalSeconds)
    {
        if (!MonitorRules.IsValidInterval(intervalSeconds))
            throw DomainValidationException.For(
                nameof(IntervalSeconds),
                $"interval must be in the range {MonitorRules.IntervalSecondsMin}..{MonitorRules.IntervalSecondsMax} seconds");

        return intervalSeconds;
    }
}
