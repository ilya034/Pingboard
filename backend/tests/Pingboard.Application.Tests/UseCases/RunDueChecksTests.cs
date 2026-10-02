using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pingboard.Application.Abstractions;
using Pingboard.Application.Probing;
using Pingboard.Application.Probing.Options;
using Pingboard.Application.Tests.Fakes;
using Pingboard.Domain.Common;

namespace Pingboard.Application.Tests.UseCases;

/// <summary>Сценарий воркера: проверяются только «просроченные» мониторы, запись — одной транзакцией.</summary>
public sealed class RunDueChecksTests
{
    private static readonly DateTimeOffset Now = new(2026, 09, 30, 12, 00, 00, TimeSpan.Zero);

    private static RunDueChecks BuildScenario(
        FakeMonitorRepository monitors,
        FakeCheckRepository checks,
        IProbeService probe,
        FakeUnitOfWork unitOfWork,
        TestTimeProvider clock)
    {
        return new RunDueChecks(
            monitors,
            checks,
            probe,
            unitOfWork,
            clock,
            Options.Create(new DueCheckOptions { BatchSize = 100, MaxParallel = 4 }),
            NullLogger<RunDueChecks>.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_ProbesOnlyDueMonitors_AndRecordsResults()
    {
        var clock = new TestTimeProvider(Now);
        var monitors = new FakeMonitorRepository();

        var due = Monitor.Create(Guid.NewGuid(), "просрочен", "https://due.example.com", 60, Now.AddHours(-1));
        var fresh = Monitor.Create(Guid.NewGuid(), "свежий", "https://fresh.example.com", 60, Now.AddHours(-1));
        fresh.RecordCheck(ProbeOutcome.Success(200, 5), Now.AddSeconds(-10));
        var disabled = Monitor.Create(Guid.NewGuid(), "выключен", "https://disabled.example.com", 60, Now.AddHours(-1),
            false);

        await monitors.AddAsync(due, CancellationToken.None);
        await monitors.AddAsync(fresh, CancellationToken.None);
        await monitors.AddAsync(disabled, CancellationToken.None);

        var checks = new FakeCheckRepository();
        var unitOfWork = new FakeUnitOfWork();
        var probe = FakeProbeService.AlwaysUp();
        var scenario = BuildScenario(monitors, checks, probe, unitOfWork, clock);

        var processed = await scenario.ExecuteAsync(CancellationToken.None);

        Assert.Equal(1, processed);
        Assert.Equal(["https://due.example.com"], probe.ProbedUrls);
        Assert.Equal(1, unitOfWork.SaveCount);

        var recorded = Assert.Single(checks.All);
        Assert.Equal(due.Id, recorded.MonitorId);
        Assert.True(recorded.Ok);
        Assert.Equal(200, recorded.StatusCode);

        // Состояние воркера ушло в сущность — то есть в БД, а не в память процесса.
        Assert.True(due.LastOk);
        Assert.Equal(clock.Now, due.LastCheckedAt);
        Assert.False(due.IsDue(clock.Now));
    }

    [Fact]
    public async Task ExecuteAsync_WhenProbeFails_StoresErrorAndMarksMonitorDown()
    {
        var clock = new TestTimeProvider(Now);
        var monitors = new FakeMonitorRepository();
        var monitor = Monitor.Create(Guid.NewGuid(), "падает", "https://down.example.com", 60, Now.AddHours(-1));
        await monitors.AddAsync(monitor, CancellationToken.None);

        var checks = new FakeCheckRepository();
        var scenario = BuildScenario(monitors, checks, FakeProbeService.AlwaysDown(), new FakeUnitOfWork(), clock);

        await scenario.ExecuteAsync(CancellationToken.None);

        var recorded = Assert.Single(checks.All);
        Assert.False(recorded.Ok);
        Assert.Equal("Таймаут 5000 мс", recorded.Error);
        Assert.False(monitor.LastOk);
    }

    [Fact]
    public async Task ExecuteAsync_WithNothingDue_DoesNotTouchDatabase()
    {
        var clock = new TestTimeProvider(Now);
        var monitors = new FakeMonitorRepository();
        var unitOfWork = new FakeUnitOfWork();
        var probe = FakeProbeService.AlwaysUp();
        var scenario = BuildScenario(monitors, new FakeCheckRepository(), probe, unitOfWork, clock);

        var processed = await scenario.ExecuteAsync(CancellationToken.None);

        Assert.Equal(0, processed);
        Assert.Empty(probe.ProbedUrls);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task ExecuteAsync_ProbesAllMonitorsThatBecameDue()
    {
        var clock = new TestTimeProvider(Now);
        var monitors = new FakeMonitorRepository();

        for (var i = 0; i < 5; i++)
            await monitors.AddAsync(
                Monitor.Create(Guid.NewGuid(), $"m{i}", $"https://m{i}.example.com", 30, Now.AddHours(-1)),
                CancellationToken.None);

        var checks = new FakeCheckRepository();
        var scenario = BuildScenario(monitors, checks, FakeProbeService.AlwaysUp(), new FakeUnitOfWork(), clock);

        var processed = await scenario.ExecuteAsync(CancellationToken.None);

        Assert.Equal(5, processed);
        Assert.Equal(5, checks.All.Count);
    }

    [Fact]
    public async Task ExecuteAsync_WhenBatchIsSmallerThanDue_PicksTheMostOverdueFirst()
    {
        var clock = new TestTimeProvider(Now);
        var monitors = new FakeMonitorRepository();

        // «Ни разу не проверенный» — самый просроченный и обязан попасть в батч первым:
        // в Postgres голое ORDER BY last_checked_at отправило бы NULL в конец (см. ListDueQuery).
        var never = Monitor.Create(Guid.NewGuid(), "никогда", "https://never.example.com", 60, Now.AddHours(-3));
        var overdue = Monitor.Create(Guid.NewGuid(), "давно", "https://overdue.example.com", 60, Now.AddHours(-3));
        overdue.RecordCheck(ProbeOutcome.Success(200, 5), Now.AddHours(-2));
        var recent = Monitor.Create(Guid.NewGuid(), "недавно", "https://recent.example.com", 60, Now.AddHours(-3));
        recent.RecordCheck(ProbeOutcome.Success(200, 5), Now.AddMinutes(-2));

        await monitors.AddAsync(never, CancellationToken.None);
        await monitors.AddAsync(overdue, CancellationToken.None);
        await monitors.AddAsync(recent, CancellationToken.None);

        var probe = FakeProbeService.AlwaysUp();
        var scenario = new RunDueChecks(
            monitors,
            new FakeCheckRepository(),
            probe,
            new FakeUnitOfWork(),
            clock,
            Options.Create(new DueCheckOptions { BatchSize = 1, MaxParallel = 1 }),
            NullLogger<RunDueChecks>.Instance);

        var processed = await scenario.ExecuteAsync(CancellationToken.None);

        Assert.Equal(1, processed);
        Assert.Equal(["https://never.example.com"], probe.ProbedUrls);
    }
}
