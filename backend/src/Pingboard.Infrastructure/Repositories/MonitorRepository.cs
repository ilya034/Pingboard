using Microsoft.EntityFrameworkCore;
using Pingboard.Application.Abstractions;
using Pingboard.Domain.Entities;
using Pingboard.Infrastructure.Persistence;

namespace Pingboard.Infrastructure.Repositories;

/// <summary>
///     Репозитории наружу отдают сущности и готовые списки — никаких IQueryable (§13 PLAN.md).
/// </summary>
public sealed class MonitorRepository(UptimeDbContext db) : IMonitorRepository
{
    public async Task AddAsync(Monitor monitor, CancellationToken ct)
    {
        await db.Monitors.AddAsync(monitor, ct);
    }

    public Task<Monitor?> GetAsync(Guid id, CancellationToken ct)
    {
        return db.Monitors.FirstOrDefaultAsync(m => m.Id == id, ct);
    }

    public async Task<IReadOnlyList<Monitor>> ListAsync(Guid ownerId, CancellationToken ct)
    {
        return await db.Monitors
            .AsNoTracking()
            .Where(m => m.OwnerId == ownerId)
            .OrderBy(m => m.Name)
            .ToListAsync(ct);
    }

    /// <summary>
    ///     «Планировщик» целиком выражен SQL-запросом: состояние расписания лежит в БД,
    ///     поэтому воркер stateless и переживает рестарт в любой момент (факторы VI, IX).
    ///     Выборка и захват — два шага, и это принципиально: между «нашёл просроченные»
    ///     и «записал результат» проходит вся итерация (батч × таймаут), поэтому без захвата
    ///     соседний воркер или перекрывшаяся итерация проверили бы те же мониторы второй раз
    ///     и записали дубль в историю. Захват — условный UPDATE: он же и проверка «всё ещё
    ///     просрочен» на актуальной версии строки, то есть CAS без явных блокировок.
    ///     Цена: если процесс умрёт сразу после захвата, монитор пропустит один интервал
    ///     (результата-то нет) — это лучше, чем дубли в истории.
    /// </summary>
    public async Task<IReadOnlyList<Monitor>> ListDueAsync(DateTimeOffset now, int batch, CancellationToken ct)
    {
        var candidates = await ListDueQuery(db, now, batch)
            .AsNoTracking()
            .Select(m => m.Id)
            .ToListAsync(ct);

        if (candidates.Count == 0) return [];

        var claimed = await db.Monitors
            .Where(m => candidates.Contains(m.Id)
                        && m.Enabled
                        && (m.LastCheckedAt == null
                            || m.LastCheckedAt.Value.AddSeconds(m.IntervalSeconds) <= now))
            .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.LastCheckedAt, now), ct);

        if (claimed == 0) return [];

        // Возвращаем именно захваченные строки: у них last_checked_at равен нашему штампу,
        // поэтому строки, которые успел захватить сосед, в выборку не попадут.
        return await db.Monitors
            .Where(m => candidates.Contains(m.Id) && m.LastCheckedAt == now)
            .ToListAsync(ct);
    }

    /// <summary>
    ///     Самые просроченные — первыми. NULL в Postgres сортируется <b>в конец</b>, поэтому
    ///     «ещё ни разу не проверенный» монитор при заполненном батче мог не проверяться никогда.
    ///     Явное сравнение с <see cref="DateTimeOffset.MinValue" /> транслируется в COALESCE и
    ///     ставит такие мониторы в начало — они и есть самые просроченные. Ровно то же выражение
    ///     повторяет <c>FakeMonitorRepository</c>, иначе фейк ведёт себя противоположно живой БД.
    ///     Метод internal static: SQL проверяется тестом без живой БД (ToQueryString).
    /// </summary>
    internal static IQueryable<Monitor> ListDueQuery(UptimeDbContext db, DateTimeOffset now, int batch)
    {
        return db.Monitors
            .Where(m => m.Enabled
                        && (m.LastCheckedAt == null
                            || m.LastCheckedAt.Value.AddSeconds(m.IntervalSeconds) <= now))
            .OrderBy(m => m.LastCheckedAt ?? DateTimeOffset.MinValue)
            .Take(batch);
    }

    public void Remove(Monitor monitor)
    {
        db.Monitors.Remove(monitor);
    }
}
