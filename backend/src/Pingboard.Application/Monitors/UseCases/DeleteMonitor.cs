using Microsoft.Extensions.Logging;
using Pingboard.Application.Abstractions;

namespace Pingboard.Application.Monitors.UseCases;

/// <summary>
///     Сценарий удаления монитора и связанной с ним истории проверок.
/// </summary>
public sealed class DeleteMonitor(
    IMonitorRepository monitors,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ILogger<DeleteMonitor>? logger = null)
{
    public async Task ExecuteAsync(Guid id, CancellationToken ct)
    {
        var monitor = await OwnedMonitor.LoadAsync(monitors, id, currentUser, ct);
        monitors.Remove(monitor);
        await unitOfWork.SaveChangesAsync(ct);

        logger?.LogInformation("Monitor deleted {MonitorId}", id);
    }
}
