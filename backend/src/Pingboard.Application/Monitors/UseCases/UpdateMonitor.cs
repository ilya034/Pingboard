using Microsoft.Extensions.Options;
using Pingboard.Application.Abstractions;
using Pingboard.Application.Monitors.Dtos;
using Pingboard.Application.Monitors.Options;
using Pingboard.Application.Validation;

namespace Pingboard.Application.Monitors.UseCases;

/// <summary>Частичное обновление монитора (PATCH-семантика: null — «не менять»).</summary>
public sealed class UpdateMonitor(
    IMonitorRepository monitors,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    IOptions<MonitorsOptions> options)
{
    public async Task<MonitorDto> ExecuteAsync(Guid id, UpdateMonitorRequest request, CancellationToken ct)
    {
        ValidationRunner.Run(new UpdateMonitorRequestValidator(), request);

        var monitor = await OwnedMonitor.LoadAsync(monitors, id, currentUser, ct);
        monitor.ApplyUpdate(request.Name, request.Url, request.IntervalSeconds, request.Enabled,
            timeProvider.GetUtcNow());

        await unitOfWork.SaveChangesAsync(ct);

        // Форма ответа та же, что у GET/POST: полоса нужной длины (сегменты «unknown»),
        // а не пустой массив — иначе клиент после PATCH рисует другую разметку.
        var now = timeProvider.GetUtcNow();

        return MonitorMapper.ToDto(monitor, MonitorMapper.EmptySummary(
            now - TimeSpan.FromHours(options.Value.UptimeWindowHours),
            now,
            options.Value.UptimeBarSegments));
    }
}
