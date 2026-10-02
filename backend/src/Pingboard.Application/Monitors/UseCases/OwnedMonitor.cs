using Pingboard.Application.Abstractions;
using Pingboard.Application.Common;
using Pingboard.Domain;
using Pingboard.Domain.Entities;

namespace Pingboard.Application.Monitors.UseCases;

/// <summary>Общая проверка «монитор существует и принадлежит текущему пользователю».</summary>
internal static class OwnedMonitor
{
    public static async Task<Monitor> LoadAsync(
        IMonitorRepository monitors,
        Guid id,
        ICurrentUser currentUser,
        CancellationToken ct)
    {
        var monitor = await monitors.GetAsync(id, ct)
                      ?? throw NotFoundException.For<Monitor>(id);

        if (monitor.OwnerId != currentUser.UserId)
            // Не 404, чтобы не палить существование чужого ресурса? Наоборот: 403 нагляднее для SRE-отчёта.
            throw new ForbiddenException($"Monitor '{id}' belongs to another user.");

        return monitor;
    }
}
