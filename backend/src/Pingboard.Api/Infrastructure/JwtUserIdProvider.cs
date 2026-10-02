using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Pingboard.Application.Abstractions;
using Pingboard.Application.Common;

namespace Pingboard.Api.Infrastructure;

/// <summary>
///     Владелец текущего запроса = claim <c>sub</c> проверенного JWT (M4).
///     Токен подписан сервером, поэтому claim можно считать доверенным; никакого
///     пользователя «по умолчанию» здесь нет — отсутствие валидного токена даёт 401,
///     а не доступ к чужому аккаунту. Сценарии Application от смены реализации не меняются.
/// </summary>
public sealed class JwtUserIdProvider(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid UserId
    {
        get
        {
            // Маппинг входящих claims отключён (MapInboundClaims = false), поэтому читаем сырой "sub".
            var raw = accessor.HttpContext?.User.FindFirstValue(JwtRegisteredClaimNames.Sub);

            return Guid.TryParse(raw, out var id)
                ? id
                : throw new UnauthorizedException(
                    "В access-токене нет claim sub с идентификатором пользователя.");
        }
    }
}
