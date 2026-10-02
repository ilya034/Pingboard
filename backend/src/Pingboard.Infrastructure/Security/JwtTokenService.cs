using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Pingboard.Application.Abstractions;
using Pingboard.Infrastructure.Security.Options;

namespace Pingboard.Infrastructure.Security;

/// <summary>
///     Выдаёт access-токен (HS256). Серверных сессий нет — процесс stateless (фактор VI).
///     Срок жизни возвращается вместе с токеном: <c>expiresAt</c> в ответе — это момент
///     истечения, а не момент выдачи (иначе фронт считает токен протухшим сразу).
/// </summary>
public sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider timeProvider) : ITokenService
{
    public IssuedToken Issue(Guid userId)
    {
        var cfg = options.Value;

        if (!JwtOptions.IsSecretValid(cfg.Secret))
            throw new InvalidOperationException("Jwt__Secret is not set or is shorter than 32 bytes — check the configuration.");

        var now = timeProvider.GetUtcNow();
        var expiresAt = now.AddMinutes(cfg.ExpiresMinutes).ToUniversalTime();

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(cfg.Secret)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            cfg.Issuer,
            cfg.Audience,
            [new Claim(JwtRegisteredClaimNames.Sub, userId.ToString())],
            now.UtcDateTime,
            expiresAt.UtcDateTime,
            credentials);

        return new IssuedToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
