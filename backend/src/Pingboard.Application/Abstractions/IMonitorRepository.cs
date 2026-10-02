namespace Pingboard.Application.Abstractions;

public interface IMonitorRepository
{
    Task AddAsync(Monitor monitor, CancellationToken ct);

    Task<Monitor?> GetAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<Monitor>> ListAsync(Guid ownerId, CancellationToken ct);

    /// <summary>
    ///     Включённые мониторы, у которых наступило время проверки (last_checked_at + interval &lt;= now
    ///     или проверок ещё не было). Это и есть весь «планировщик» — фактор VI.
    /// </summary>
    Task<IReadOnlyList<Monitor>> ListDueAsync(DateTimeOffset now, int batch, CancellationToken ct);

    void Remove(Monitor monitor);
}