namespace Pingboard.Application.Abstractions;

public interface IMonitorRepository
{
    Task AddAsync(Monitor monitor, CancellationToken ct);

    Task<Monitor?> GetAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<Monitor>> ListAsync(Guid ownerId, CancellationToken ct);

    /// <summary>
    ///     Возвращает активные мониторы, для которых подошло время очередной проверки.
    /// </summary>
    /// <param name="now">Текущая временная метка.</param>
    /// <param name="batch">Максимальное количество мониторов в выборке.</param>
    /// <param name="ct">Токен отмены операции.</param>
    Task<IReadOnlyList<Monitor>> ListDueAsync(DateTimeOffset now, int batch, CancellationToken ct);

    void Remove(Monitor monitor);
}