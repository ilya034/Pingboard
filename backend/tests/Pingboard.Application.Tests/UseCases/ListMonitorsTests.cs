using Microsoft.Extensions.Options;
using Pingboard.Application.Abstractions;
using Pingboard.Application.Monitors.Dtos;
using Pingboard.Application.Monitors.Options;
using Pingboard.Application.Monitors.UseCases;
using Pingboard.Application.Probing;
using Pingboard.Application.Tests.Fakes;
using Pingboard.Domain.Common;
using Pingboard.Domain.Entities;

namespace Pingboard.Application.Tests.UseCases;

/// <summary>Список мониторов: uptime и полоса доступности считаются из истории проверок.</summary>
public sealed class ListMonitorsTests
{
    private static readonly DateTimeOffset Now = new(2026, 09, 30, 12, 00, 00, TimeSpan.Zero);
    private static readonly Guid Owner = Guid.NewGuid();

    [Fact]
    public async Task ExecuteAsync_ComputesUptimeAndBarFromHistory()
    {
        var monitors = new FakeMonitorRepository();
        var monitor = Monitor.Create(Owner, "блог", "https://example.com", 60, Now.AddHours(-2));
        await monitors.AddAsync(monitor, CancellationToken.None);

        var checks = new FakeCheckRepository();
        checks.Seed(
        [
            CheckResult.FromProbe(monitor.Id, Now.AddHours(-1.5), ProbeOutcome.Success(200, 10)),
            CheckResult.FromProbe(monitor.Id, Now.AddHours(-1.4), ProbeOutcome.Success(200, 12)),
            CheckResult.FromProbe(monitor.Id, Now.AddHours(-1.3), ProbeOutcome.BadStatus(500, 20)),
            CheckResult.FromProbe(monitor.Id, Now.AddHours(-1.2), ProbeOutcome.Success(200, 11))
        ]);

        var scenario = new ListMonitors(
            monitors,
            checks,
            new FakeCurrentUser(Owner),
            new TestTimeProvider(Now),
            Options.Create(new MonitorsOptions()));

        var result = await scenario.ExecuteAsync(CancellationToken.None);

        var dto = Assert.Single(result);
        Assert.Equal(24, dto.UptimeBar.Count);
        Assert.Equal(0.75, dto.Uptime24h);
        Assert.Equal(11, dto.LastLatencyMs);

        // Проверки легли в предпоследний час окна (Now-2h .. Now).
        var bucketWithData = Assert.Single(dto.UptimeBar, b => b.Total > 0);
        Assert.Equal("down", bucketWithData.State);
        Assert.Equal(4, bucketWithData.Total);
        Assert.Equal(1, bucketWithData.Failed);
    }

    [Fact]
    public async Task ExecuteAsync_WithNoChecks_LeavesUptimeUnknown()
    {
        var monitors = new FakeMonitorRepository();
        await monitors.AddAsync(Monitor.Create(Owner, "блог", "https://example.com", 60, Now), CancellationToken.None);

        var scenario = new ListMonitors(
            monitors,
            new FakeCheckRepository(),
            new FakeCurrentUser(Owner),
            new TestTimeProvider(Now),
            Options.Create(new MonitorsOptions()));

        var dto = Assert.Single(await scenario.ExecuteAsync(CancellationToken.None));

        Assert.Null(dto.Uptime24h);
        Assert.Null(dto.LastLatencyMs);
        Assert.All(dto.UptimeBar, bucket => Assert.Equal(0, bucket.Total));
    }

    [Fact]
    public async Task ExecuteAsync_TakesUptimeFromTheBatchQuery_NotPerMonitor()
    {
        var monitors = new FakeMonitorRepository();
        var monitor = Monitor.Create(Owner, "блог", "https://example.com", 60, Now.AddHours(-2));
        await monitors.AddAsync(monitor, CancellationToken.None);

        var checks = new FakeCheckRepository();
        checks.Seed([CheckResult.FromProbe(monitor.Id, Now.AddHours(-1), ProbeOutcome.Success(200, 10))]);

        var scenario = new ListMonitors(
            monitors,
            checks,
            new FakeCurrentUser(Owner),
            new TestTimeProvider(Now),
            Options.Create(new MonitorsOptions()));

        var dto = Assert.Single(await scenario.ExecuteAsync(CancellationToken.None));

        Assert.Equal(1.0, dto.Uptime24h);

        // UptimeRatioAsync делает по два CountAsync на вызов: в цикле по мониторам это
        // 2N+1 запрос на открытие дашборда. Дашборд обязан пользоваться пакетным запросом.
        Assert.Equal(0, checks.SingleUptimeCalls);
    }

    [Fact]
    public async Task ExecuteAsync_KeepsSparseMonitorHistory_WhenNeighbourMonitorIsChatty()
    {
        var monitors = new FakeMonitorRepository();
        var chatty = Monitor.Create(Owner, "частый", "https://chatty.example.com", 30, Now.AddHours(-2));
        var sparse = Monitor.Create(Owner, "редкий", "https://sparse.example.com", 3600, Now.AddHours(-2));
        await monitors.AddAsync(chatty, CancellationToken.None);
        await monitors.AddAsync(sparse, CancellationToken.None);

        var checks = new FakeCheckRepository();
        checks.Seed(Enumerable.Range(0, 20)
            .Select(i => CheckResult.FromProbe(chatty.Id, Now.AddMinutes(-i), ProbeOutcome.Success(200, 10 + i))));
        checks.Seed([CheckResult.FromProbe(sparse.Id, Now.AddMinutes(-30), ProbeOutcome.BadStatus(500, 40))]);

        var scenario = new ListMonitors(
            monitors,
            checks,
            new FakeCurrentUser(Owner),
            new TestTimeProvider(Now),
            Options.Create(new MonitorsOptions { DashboardHistoryPerMonitor = 2 }));

        var result = await scenario.ExecuteAsync(CancellationToken.None);
        var sparseDto = result.Single(m => m.Id == sparse.Id);

        // Лимит истории — на каждый монитор: при глобальном Take(лимит × N) «жирный» монитор
        // вытесняет редкий из выборки, и тот показывается серым без данных, хотя проверки есть.
        Assert.Equal(40, sparseDto.LastLatencyMs);
        Assert.Contains(sparseDto.UptimeBar, bucket => bucket.Total > 0);
        Assert.Equal(0.0, sparseDto.Uptime24h);
    }
}
