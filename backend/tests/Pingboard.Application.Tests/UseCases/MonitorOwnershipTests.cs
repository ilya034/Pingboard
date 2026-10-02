using Microsoft.Extensions.Options;
using Pingboard.Application.Abstractions;
using Pingboard.Application.Common;
using Pingboard.Application.Monitors.Options;
using Pingboard.Application.Monitors.UseCases;
using Pingboard.Application.Tests.Fakes;
using Pingboard.Domain;
using Pingboard.Domain.Entities;

namespace Pingboard.Application.Tests.UseCases;

/// <summary>Изоляция по владельцу: чужой монитор — 403, несуществующий — 404.</summary>
public sealed class MonitorOwnershipTests
{
    private static readonly DateTimeOffset Now = new(2026, 09, 30, 12, 00, 00, TimeSpan.Zero);

    [Fact]
    public async Task GetMonitor_ForAnotherOwner_ThrowsForbidden()
    {
        var monitors = new FakeMonitorRepository();
        var monitor = Monitor.Create(Guid.NewGuid(), "чужой", "https://example.com", 60, Now);
        await monitors.AddAsync(monitor, CancellationToken.None);

        var scenario = new GetMonitor(
            monitors,
            new FakeCheckRepository(),
            new FakeCurrentUser(Guid.NewGuid()),
            new TestTimeProvider(Now),
            Options.Create(new MonitorsOptions()));

        await Assert.ThrowsAsync<ForbiddenException>(() => scenario.ExecuteAsync(monitor.Id, CancellationToken.None));
    }

    [Fact]
    public async Task GetMonitor_ForUnknownId_ThrowsNotFound()
    {
        var scenario = new GetMonitor(
            new FakeMonitorRepository(),
            new FakeCheckRepository(),
            new FakeCurrentUser(Guid.NewGuid()),
            new TestTimeProvider(Now),
            Options.Create(new MonitorsOptions()));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            scenario.ExecuteAsync(Guid.NewGuid(), CancellationToken.None));
    }
}
