using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Pingboard.Application.Abstractions;
using Pingboard.Application.Common;

namespace Pingboard.Api.Infrastructure;

/// <summary>
/// Извлекает идентификатор текущего пользователя из утверждения <c>sub</c> проверенного JWT-токена в текущем HttpContext.
/// Выбрасывает <see cref="UnauthorizedException"/>, если токен не содержит валидного GUID пользователя.
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
                    "No sub claim with a user ID was found in the access token.");
        }
    }
}
