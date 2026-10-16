namespace Pingboard.Application.Abstractions;

/// <summary>
/// Абстракция проверки доступности базы данных для зонда готовности к обработке трафика (readiness probe).
/// Изолирует API от деталей реализации доступа к данным.
/// </summary>
public interface IDatabaseHealthProbe
{
    /// <summary>
    /// Проверяет возможность установки соединения с базой данных (SELECT 1).
    /// </summary>
    Task<bool> CanConnectAsync(CancellationToken ct);
}
