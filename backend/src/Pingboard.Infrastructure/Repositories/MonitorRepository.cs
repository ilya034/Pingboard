using Microsoft.EntityFrameworkCore;
using Pingboard.Application.Abstractions;
using Pingboard.Domain.Entities;
using Pingboard.Infrastructure.Persistence;

namespace Pingboard.Infrastructure.Repositories;

/// <summary>
///     Репозиторий сущностей <see cref="Monitor" /> на базе EF Core.
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
    ///     Выбирает и атомарно захватывает пачку мониторов, готовых к очередной проверке.
    ///     Захват выполняется через условный UPDATE (CAS-семантика), что исключает
    ///     конкурентную обработку одних и тех же записей параллельными воркерами.
    /// </summary>
    /// <param name="now">Текущая временная метка.</param>
    /// <param name="batch">Максимальный размер батча.</param>
    /// <param name="ct">Токен отмены операции.</param>
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
