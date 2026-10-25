using Microsoft.Extensions.Logging;
using Pingboard.Application.Abstractions;
using Pingboard.Application.Observability;

namespace Pingboard.Application.Monitors.UseCases;

/// <summary>
///     Сценарий удаления монитора и связанной с ним истории проверок.
/// </summary>
public sealed class DeleteMonitor(
    IMonitorRepository monitors,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ILogger<DeleteMonitor>? logger = null,
    IPingboardMetrics? metrics = null)
{
    public async Task ExecuteAsync(Guid id, CancellationToken ct)
    {
        var monitor = await OwnedMonitor.LoadAsync(monitors, id, currentUser, ct);
        monitors.Remove(monitor);
        await unitOfWork.SaveChangesAsync(ct);

        (metrics ?? PingboardMetrics.Default).RecordMonitorDeleted();
        logger?.LogInformation("Monitor deleted {MonitorId}", id);
    }
}
