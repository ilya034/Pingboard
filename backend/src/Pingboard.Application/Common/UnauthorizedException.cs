namespace Pingboard.Application.Common;

/// <summary>
///     Требуется аутентификация (401). Отдельно от <see cref="ForbiddenException" /> (403):
///     401 означает «не предъявлен валидный токен», 403 — «токен валиден, но ресурс чужой».
///     Клиент по этим кодам принимает разные решения (обновить токен vs не показывать ресурс).
/// </summary>
public class UnauthorizedException : Exception
{
    public UnauthorizedException(string message) : base(message)
    {
    }
}
