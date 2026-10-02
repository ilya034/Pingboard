using Microsoft.EntityFrameworkCore;
using Pingboard.Application.Abstractions;
using Pingboard.Domain.Entities;
using Pingboard.Infrastructure.Persistence;

namespace Pingboard.Infrastructure.Repositories;

/// <summary>
///     Одна транзакция на сценарий. DbContext scoped — в Api это scope запроса,
///     в воркере — scope итерации цикла.
/// </summary>
public sealed class UnitOfWork(UptimeDbContext db) : IUnitOfWork
{
    /// <summary>
    ///     Сохранение с одной осознанной уступкой: если монитор удалили, пока итерация шла
    ///     (кнопка в UI, второй процесс, ручной DELETE), сохранение падает целиком — вместе
    ///     с результатами проверок всех остальных мониторов батча. Здесь такие «осиротевшие»
    ///     результаты убираются из трекинга, и сохранение повторяется; всё остальное
    ///     (в том числе нарушение UNIQUE на email при регистрации) пробрасывается наружу
    ///     как раньше — фильтр не глотает ошибки, а только лечит конкретный случай.
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
    ///     Убирает из трекинга записи, чьих мониторов в БД уже нет, и сообщает, было ли
    ///     что убирать (false — значит ошибка не про удалённый монитор, её нужно пробросить).
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
