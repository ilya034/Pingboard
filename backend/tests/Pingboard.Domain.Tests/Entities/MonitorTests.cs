using Pingboard.Domain.Common;
using Pingboard.Domain.Entities;

namespace Pingboard.Domain.Tests.Entities;

/// <summary>
///     Валидация — правило домена, поэтому и тесты живут в Domain.Tests, без БД и моков.
/// </summary>
public sealed class MonitorTests
{
    private static readonly DateTimeOffset Now = new(2026, 09, 30, 12, 00, 00, TimeSpan.Zero);

    [Fact]
    public void Create_WithValidData_TrimsNameAndStoresFields()
    {
        var monitor = Monitor.Create(Guid.NewGuid(), "  Мой блог  ", "https://example.com", 60, Now);

        Assert.Equal("Мой блог", monitor.Name);
        Assert.Equal("https://example.com", monitor.Url);
        Assert.Equal(60, monitor.IntervalSeconds);
        Assert.True(monitor.Enabled);
        Assert.Equal(Now, monitor.CreatedAt);
        Assert.Equal(Now, monitor.UpdatedAt);
        Assert.Null(monitor.LastCheckedAt);
        Assert.Null(monitor.LastOk);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithEmptyName_Throws(string name)
    {
        Assert.Throws<DomainValidationException>(() =>
            Monitor.Create(Guid.NewGuid(), name, "https://example.com", 60, Now));
    }

    [Fact]
    public void Create_WithTooLongName_Throws()
    {
        var name = new string('a', MonitorRules.NameMaxLength + 1);

        Assert.Throws<DomainValidationException>(() =>
            Monitor.Create(Guid.NewGuid(), name, "https://example.com", 60, Now));
    }

    [Theory]
    [InlineData("example.com")] // не абсолютный
    [InlineData("/relative")] // не абсолютный
    [InlineData("ftp://example.com")] // не http/https
    [InlineData("not a url")]
    [InlineData("")]
    public void Create_WithUnsupportedUrl_Throws(string url)
    {
        Assert.Throws<DomainValidationException>(() =>
            Monitor.Create(Guid.NewGuid(), "name", url, 60, Now));
    }

    [Theory]
    [InlineData(MonitorRules.IntervalSecondsMin - 1)]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(MonitorRules.IntervalSecondsMax + 1)]
    public void Create_WithIntervalOutOfRange_Throws(int interval)
    {
        Assert.Throws<DomainValidationException>(() =>
            Monitor.Create(Guid.NewGuid(), "name", "https://example.com", interval, Now));
    }

    [Theory]
    [InlineData(MonitorRules.IntervalSecondsMin)]
    [InlineData(60)]
    [InlineData(MonitorRules.IntervalSecondsMax)]
    public void Create_WithBoundaryInterval_Succeeds(int interval)
    {
        var monitor = Monitor.Create(Guid.NewGuid(), "name", "https://example.com", interval, Now);

        Assert.Equal(interval, monitor.IntervalSeconds);
    }

    [Fact]
    public void Create_WithEmptyOwner_Throws()
    {
        Assert.Throws<DomainValidationException>(() =>
            Monitor.Create(Guid.Empty, "name", "https://example.com", 60, Now));
    }

    [Fact]
    public void ApplyUpdate_ChangesOnlyProvidedFields_AndBumpsUpdatedAt()
    {
        var monitor = Monitor.Create(Guid.NewGuid(), "name", "https://example.com", 60, Now);
        var later = Now.AddMinutes(10);

        monitor.ApplyUpdate(null, "https://example.org", null, null, later);

        Assert.Equal("name", monitor.Name);
        Assert.Equal("https://example.org", monitor.Url);
        Assert.Equal(60, monitor.IntervalSeconds);
        Assert.Equal(later, monitor.UpdatedAt);
    }

    [Fact]
    public void ApplyUpdate_WithInvalidValue_ThrowsAndLeavesEntityUntouched()
    {
        var monitor = Monitor.Create(Guid.NewGuid(), "name", "https://example.com", 60, Now);

        // Валидация идёт ДО мутаций: имя не должно «полупроставиться».
        Assert.Throws<DomainValidationException>(() =>
            monitor.ApplyUpdate("новое имя", "ftp://bad", null, null, Now.AddMinutes(1)));

        Assert.Equal("name", monitor.Name);
        Assert.Equal("https://example.com", monitor.Url);
        Assert.Equal(Now, monitor.UpdatedAt);
    }

    [Fact]
    public void ApplyUpdate_WithNoChanges_DoesNotBumpUpdatedAt()
    {
        var monitor = Monitor.Create(Guid.NewGuid(), "name", "https://example.com", 60, Now);

        monitor.ApplyUpdate("name", "https://example.com", 60, true, Now.AddHours(1));

        Assert.Equal(Now, monitor.UpdatedAt);
    }

    [Fact]
    public void RecordCheck_MovesNextCheckDueAt()
    {
        var monitor = Monitor.Create(Guid.NewGuid(), "name", "https://example.com", 60, Now);

        // Пока проверок не было, «следующая проверка» — это минимальное время: монитор due сразу.
        Assert.True(monitor.IsDue(Now));

        var checkedAt = Now.AddSeconds(65);
        monitor.RecordCheck(ProbeOutcome.Success(200, 12), checkedAt);

        Assert.Equal(checkedAt, monitor.LastCheckedAt);
        Assert.True(monitor.LastOk);
        Assert.Equal(checkedAt.AddSeconds(60), monitor.NextCheckDueAt);
        Assert.False(monitor.IsDue(checkedAt));
        Assert.True(monitor.IsDue(checkedAt.AddSeconds(60)));
    }

    [Fact]
    public void IsDue_WhenDisabled_IsAlwaysFalse()
    {
        var monitor = Monitor.Create(Guid.NewGuid(), "name", "https://example.com", 60, Now);
        monitor.SetEnabled(false, Now);

        Assert.False(monitor.IsDue(Now.AddDays(1)));
    }
}
