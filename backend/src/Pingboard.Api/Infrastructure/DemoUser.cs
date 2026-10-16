namespace Pingboard.Api.Infrastructure;

/// <summary>
/// Учётные данные демонстрационного пользователя для локальной разработки (SeedOnStart).
/// </summary>
public static class DemoUser
{
    /// <summary>Стабильный идентификатор демонстрационного пользователя.</summary>
    public static readonly Guid Id = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public const string Email = "demo@pingboard.local";

    public const string Password = "demo-password";
}
