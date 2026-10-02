using Pingboard.Domain.Common;
using Pingboard.Domain.Entities;

namespace Pingboard.Domain.Tests.Entities;

public sealed class CheckResultTests
{
    private static readonly DateTimeOffset Now = new(2026, 09, 30, 12, 00, 00, TimeSpan.Zero);

    [Fact]
    public void FromProbe_StoresProbeFields()
    {
        var monitorId = Guid.NewGuid();

        var check = CheckResult.FromProbe(monitorId, Now, ProbeOutcome.Success(200, 42));

        Assert.Equal(monitorId, check.MonitorId);
        Assert.Equal(Now, check.CheckedAt);
        Assert.True(check.Ok);
        Assert.Equal(200, check.StatusCode);
        Assert.Equal(42, check.LatencyMs);
        Assert.Null(check.Error);
    }

    [Fact]
    public void FromProbe_WithFailure_KeepsErrorAndNullStatus()
    {
        var check = CheckResult.FromProbe(Guid.NewGuid(), Now, ProbeOutcome.Failure("Таймаут 5000 мс"));

        Assert.False(check.Ok);
        Assert.Null(check.StatusCode);
        Assert.Equal("Таймаут 5000 мс", check.Error);
    }

    [Fact]
    public void FromProbe_WithEmptyMonitor_Throws()
    {
        Assert.Throws<DomainValidationException>(() =>
            CheckResult.FromProbe(Guid.Empty, Now, ProbeOutcome.Success(200, 1)));
    }
}
