using Pingboard.Application.Abstractions;
using Pingboard.Domain.Entities;

namespace Pingboard.Application.Tests.Fakes;

/// <summary>
///     Фейк порта мониторов: in-memory список плюс копия «планировщика» так, как его
///     реализует Infrastructure.MonitorRepository.ListDueAsync — это и есть контракт порта,
///     который обязана соблюдать любая реализация.
/// </summary>
public sealed class FakeMonitorRepository : IMonitorRepository
{
    private readonly List<Monitor> _monitors = [];

    public IReadOnlyList<Monitor> All => _monitors;

    public IReadOnlyList<Monitor> Removed { get; private set; } = [];

    public Task AddAsync(Monitor monitor, CancellationToken ct)
    {
        _monitors.Add(monitor);
        return Task.CompletedTask;
    }

    public Task<Monitor?> GetAsync(Guid id, CancellationToken ct)
    {
        return Task.FromResult(_monitors.FirstOrDefault(m => m.Id == id));
    }

    public Task<IReadOnlyList<Monitor>> ListAsync(Guid ownerId, CancellationToken ct)
    {
        return Task.FromResult<IReadOnlyList<Monitor>>(
            _monitors.Where(m => m.OwnerId == ownerId).OrderBy(m => m.Name).ToArray());
    }

    /// <summary>
    ///     Выражение сортировки намеренно совпадает символ в символ с SQL-версией
    ///     (<c>MonitorRepository.ListDueQuery</c>): в LINQ-to-objects null идёт в начало,
    ///     в Postgres — в конец, и без явного <c>?? MinValue</c> фейк вёл бы себя
    ///     противоположно живой БД, а тесты этого не видели бы.
    /// </summary>
    public Task<IReadOnlyList<Monitor>> ListDueAsync(DateTimeOffset now, int batch, CancellationToken ct)
    {
        return Task.FromResult<IReadOnlyList<Monitor>>(
            _monitors
                .Where(m => m.Enabled && (m.LastCheckedAt is null ||
                                          m.LastCheckedAt.Value.AddSeconds(m.IntervalSeconds) <= now))
                .OrderBy(m => m.LastCheckedAt ?? DateTimeOffset.MinValue)
                .Take(batch)
                .ToArray());
    }

    public void Remove(Monitor monitor)
    {
        _monitors.Remove(monitor);
        Removed = [.. Removed, monitor];
    }
}
