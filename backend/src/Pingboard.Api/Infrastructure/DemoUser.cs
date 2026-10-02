namespace Pingboard.Api.Infrastructure;

/// <summary>
///     Учётные данные демо-пользователя, которого создаёт сид на старте (SeedOnStart).
///     Нужны, чтобы после включения JWT (M4) было чем войти в свежеподнятый стенд:
///     POST /api/auth/login с этими email/паролем → access-токен.
/// </summary>
public static class DemoUser
{
    /// <summary>Стабильный GUID: связывает сид, appsettings и .env.example.</summary>
    public static readonly Guid Id = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public const string Email = "demo@pingboard.local";

    public const string Password = "demo-password";
}
