using Microsoft.Extensions.Options;
using Pingboard.Application.Abstractions;
using Pingboard.Application.Validation;
using Pingboard.Application.Monitors.Dtos;
using Pingboard.Application.Monitors.Options;
using Pingboard.Domain.Common;
using Pingboard.Domain.Entities;

namespace Pingboard.Application.Monitors.UseCases;

/// <summary>
///     Сценарии над мониторами: обычные классы с явными зависимостями.
///     MediatR/CQRS-пайплайн сознательно не тянем (см. §3 PLAN.md) — на MVP это лишняя церемония.
///     Каждый сценарий регистрируется в DI как scoped (см. DependencyInjection.cs).
/// </summary>
public sealed class CreateMonitor(
    IMonitorRepository monitors,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    IOptions<MonitorsOptions> options)
{
    public async Task<MonitorDto> ExecuteAsync(CreateMonitorRequest request, CancellationToken ct)
    {
        ValidationRunner.Run(new CreateMonitorRequestValidator(), request);

        // Часы читаются один раз: три обращения к timeProvider подряд давали три разных
        // «сейчас» в одном ответе (CreatedAt монитора и границы полосы).
        var now = timeProvider.GetUtcNow();

        var monitor = Monitor.Create(
            currentUser.UserId,
            request.Name!,
            request.Url!,
            request.IntervalSeconds ?? MonitorRules.IntervalSecondsDefault,
            now,
            request.Enabled ?? true);

        await monitors.AddAsync(monitor, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return MonitorMapper.ToDto(monitor, BuildEmptySummary(now));
    }

    private MonitorStatusSummary BuildEmptySummary(DateTimeOffset now)
    {
        return MonitorMapper.EmptySummary(
            now - TimeSpan.FromHours(options.Value.UptimeWindowHours),
            now,
            options.Value.UptimeBarSegments);
    }
}
