namespace Pingboard.Application.Abstractions;

/// <summary>
///     Готовность зависимости: доступна ли БД (readiness-проба).
///     Портом закрывается правило «в Api нет ни одного типа EF Core» (§2 README):
///     endpoint спрашивает слой Infrastructure, а не <c>UptimeDbContext</c> напрямую.
/// </summary>
public interface IDatabaseHealthProbe
{
    /// <summary>true — соединение с БД устанавливается (SELECT 1). Исключения наружу не летят.</summary>
    Task<bool> CanConnectAsync(CancellationToken ct);
}
