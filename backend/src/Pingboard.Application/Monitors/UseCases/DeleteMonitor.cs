using Pingboard.Application.Abstractions;

namespace Pingboard.Application.Monitors.UseCases;

/// <summary>Удаление монитора: история проверок уходит каскадом в БД.</summary>
public sealed class DeleteMonitor(
    IMonitorRepository monitors,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser)
{
    public async Task ExecuteAsync(Guid id, CancellationToken ct)
    {
        var monitor = await OwnedMonitor.LoadAsync(monitors, id, currentUser, ct);
        monitors.Remove(monitor);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
