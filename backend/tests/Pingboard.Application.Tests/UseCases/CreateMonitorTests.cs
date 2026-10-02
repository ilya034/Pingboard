using Microsoft.Extensions.Options;
using Pingboard.Application.Common;
using Pingboard.Application.Monitors.Dtos;
using Pingboard.Application.Monitors.Options;
using Pingboard.Application.Monitors.UseCases;
using Pingboard.Application.Tests.Fakes;
using Pingboard.Domain.Common;
using Pingboard.Domain.Entities;

namespace Pingboard.Application.Tests.UseCases;

public sealed class CreateMonitorTests
{
    private static readonly DateTimeOffset Now = new(2026, 09, 30, 12, 00, 00, TimeSpan.Zero);
    private static readonly Guid Owner = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static CreateMonitor BuildScenario(
        FakeMonitorRepository monitors,
        FakeUnitOfWork unitOfWork,
        out TestTimeProvider clock)
    {
        clock = new TestTimeProvider(Now);

        return new CreateMonitor(
            monitors,
            unitOfWork,
            new FakeCurrentUser(Owner),
            clock,
            Options.Create(new MonitorsOptions()));
    }

    [Fact]
    public async Task ExecuteAsync_WithValidRequest_PersistsMonitorAndSavesOnce()
    {
        var monitors = new FakeMonitorRepository();
        var unitOfWork = new FakeUnitOfWork();
        var scenario = BuildScenario(monitors, unitOfWork, out _);

        var dto = await scenario.ExecuteAsync(new CreateMonitorRequest("РњРѕР№ Р±Р»РѕРі", "https://example.com", 30, null),
            CancellationToken.None);

        Assert.Single(monitors.All);
        Assert.Equal(1, unitOfWork.SaveCount);
        Assert.Equal(Owner, monitors.All[0].OwnerId);
        Assert.Equal(30, dto.IntervalSeconds);
        Assert.True(dto.Enabled);
        Assert.Null(dto.Uptime24h);
        // РџРѕР»РѕСЃР° РґРѕСЃС‚СѓРїРЅРѕСЃС‚Рё РµСЃС‚СЊ СЃСЂР°Р·Сѓ: 24 СЃРµРіРјРµРЅС‚Р° РїРѕ СѓРјРѕР»С‡Р°РЅРёСЋ.
        Assert.Equal(24, dto.UptimeBar.Count);
        Assert.All(dto.UptimeBar, bucket => Assert.Equal("unknown", bucket.State));
    }

    [Fact]
    public async Task ExecuteAsync_WithoutInterval_UsesDefault()
    {
        var monitors = new FakeMonitorRepository();
        var scenario = BuildScenario(monitors, new FakeUnitOfWork(), out _);

        var dto = await scenario.ExecuteAsync(new CreateMonitorRequest("Р‘Р»РѕРі", "https://example.com", null, null),
            CancellationToken.None);

        Assert.Equal(MonitorRules.IntervalSecondsDefault, dto.IntervalSeconds);
    }

    [Fact]
    public async Task ExecuteAsync_WithInvalidUrl_ThrowsValidationAndWritesNothing()
    {
        var monitors = new FakeMonitorRepository();
        var unitOfWork = new FakeUnitOfWork();
        var scenario = BuildScenario(monitors, unitOfWork, out _);

        var exception = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            scenario.ExecuteAsync(new CreateMonitorRequest("Р‘Р»РѕРі", "ftp://example.com", null, null),
                CancellationToken.None));

        Assert.Contains("url", exception.Errors.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Empty(monitors.All);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task ExecuteAsync_WithTooShortInterval_ReportsIntervalErrorOnly()
    {
        var scenario = BuildScenario(new FakeMonitorRepository(), new FakeUnitOfWork(), out _);

        var exception = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            scenario.ExecuteAsync(new CreateMonitorRequest("Р‘Р»РѕРі", "https://example.com", 5, null),
                CancellationToken.None));

        Assert.Single(exception.Errors);
        Assert.Contains("intervalSeconds", exception.Errors.Keys, StringComparer.OrdinalIgnoreCase);
    }
}
