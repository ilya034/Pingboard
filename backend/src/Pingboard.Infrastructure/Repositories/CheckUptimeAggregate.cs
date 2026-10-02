namespace Pingboard.Infrastructure.Repositories;

/// <summary>
///     Проекция агрегата uptime по монитору (<c>GROUP BY monitor_id</c>): сколько всего
///     проверок за окно и сколько из них успешных. Нужна как именованный тип, потому что
///     анонимный тип нельзя вернуть из метода-построителя запроса.
/// </summary>
internal sealed record CheckUptimeAggregate(Guid MonitorId, int Total, int Ok);
