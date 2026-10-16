using Microsoft.EntityFrameworkCore;
using Pingboard.Application.Abstractions;
using Pingboard.Domain.Entities;
using Pingboard.Infrastructure.Persistence;

namespace Pingboard.Infrastructure.Repositories;

/// <summary>
/// Реализация паттерна Unit of Work на базе контекста Entity Framework Core.
/// Обеспечивает сохранение изменений текущей транзакции/области видимости.
/// </summary>
public sealed class UnitOfWork(UptimeDbContext db) : IUnitOfWork
{
    /// <summary>
    /// Сохраняет отслеживаемые изменения в базе данных. При возникновении ошибок внешнего ключа
    /// из-за параллельного удаления монитора отсоединяет осиротевшие сущности и повторяет попытку сохранения.
    /// </summary>
    public async Task SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is DbUpdateException or DbUpdateConcurrencyException)
        {
            if (!await DropResultsOfVanishedMonitorsAsync(ct)) throw;

            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Исключает из ChangeTracker записи проверок и изменения мониторов, удалённых из БД параллельной операцией.
    /// Возвращает true, если были найдены и исключены осиротевшие сущности.
    /// </summary>
    private async Task<bool> DropResultsOfVanishedMonitorsAsync(CancellationToken ct)
    {
        var addedChecks = db.ChangeTracker.Entries<CheckResult>()
            .Where(e => e.State == EntityState.Added)
            .ToArray();

        var modifiedMonitors = db.ChangeTracker.Entries<Monitor>()
            .Where(e => e.State is EntityState.Modified or EntityState.Deleted)
            .ToArray();

        var touched = modifiedMonitors.Select(e => e.Entity.Id)
            .Concat(addedChecks.Select(e => e.Entity.MonitorId))
            .Distinct()
            .ToArray();

        if (touched.Length == 0) return false;

        var alive = (await db.Monitors
                .AsNoTracking()
                .Where(m => touched.Contains(m.Id))
                .Select(m => m.Id)
                .ToListAsync(ct))
            .ToHashSet();

        var dropped = modifiedMonitors.Count(entry => !alive.Contains(entry.Entity.Id));

        foreach (var entry in modifiedMonitors.Where(e => !alive.Contains(e.Entity.Id)))
            entry.State = EntityState.Detached;

        foreach (var entry in addedChecks.Where(e => !alive.Contains(e.Entity.MonitorId)))
        {
            entry.State = EntityState.Detached;
            dropped++;
        }

        return dropped > 0;
    }
}
