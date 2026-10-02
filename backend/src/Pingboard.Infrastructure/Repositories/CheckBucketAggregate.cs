namespace Pingboard.Infrastructure.Repositories;

/// <summary>
///     Проекция «сколько проверок и сколько неуспешных в сегменте окна» (<c>GROUP BY</c> по
///     монитору и номеру сегмента). Нужна как именованный тип, потому что анонимный тип
///     нельзя вернуть из метода-построителя запроса.
/// </summary>
internal sealed record CheckBucketAggregate(Guid MonitorId, int Index, int Total, int Failed);
