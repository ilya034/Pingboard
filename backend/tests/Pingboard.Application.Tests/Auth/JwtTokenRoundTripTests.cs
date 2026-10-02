using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Pingboard.Api.Infrastructure;
using Pingboard.Application.Tests.Fakes;
using Pingboard.Infrastructure.Security;
using Pingboard.Infrastructure.Security.Options;

namespace Pingboard.Application.Tests.Auth;

/// <summary>
///     Проверяет самое хрупкое место JWT: что токен, выписанный <see cref="JwtTokenService" />,
///     принимается ровно теми параметрами, которыми его проверяет Api, и что владелец
///     (claim sub) при этом доходит до кода без искажений.
///     Раньше это не проверялось вообще, а расхождение здесь означает либо всех с 401,
///     либо (хуже) приём токенов, подписанных чужим ключом.
/// </summary>
public sealed class JwtTokenRoundTripTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    private static JwtOptions ValidOptions() => new()
    {
        Secret = "unit-test-secret-value-0123456789abcdef",
        Issuer = "pingboard",
        Audience = "pingboard",
        ExpiresMinutes = 120
    };

    private static string Issue(JwtOptions options, Guid userId, DateTimeOffset now)
    {
        var issued = new JwtTokenService(Options.Create(options), new TestTimeProvider(now)).Issue(userId);

        // Срок жизни возвращается вместе с токеном — этим полем пользуется AuthTokenDto.
        Assert.Equal(now.AddMinutes(options.ExpiresMinutes), issued.ExpiresAt);

        return issued.AccessToken;
    }

    private static JwtSecurityTokenHandler Handler()
    {
        return new JwtSecurityTokenHandler { MapInboundClaims = false };
    }

    /// <summary>
    ///     Параметры проверки те же, что у Api (<see cref="AuthenticationSetup" />),
    ///     но «текущее время» подменено: иначе просроченный токен не проверить.
    /// </summary>
    private static TokenValidationParameters ValidationParameters(JwtOptions options, DateTimeOffset now)
    {
        var parameters = AuthenticationSetup.TokenValidationParametersFor(options);
        parameters.LifetimeValidator = (_, expires, _, _) => expires > now;

        return parameters;
    }

    [Fact]
    public void Issued_token_is_accepted_by_api_validation_parameters()
    {
        var options = ValidOptions();
        var token = Issue(options, Guid.NewGuid(), Now);

        var principal = Handler().ValidateToken(token, ValidationParameters(options, Now), out _);

        Assert.NotNull(principal);
    }

    [Fact]
    public void Issued_token_carries_owner_id_in_sub_claim()
    {
        var options = ValidOptions();
        var userId = Guid.NewGuid();
        var token = Issue(options, userId, Now);

        var principal = Handler().ValidateToken(token, ValidationParameters(options, Now), out _);

        // MapInboundClaims = false: claim остаётся "sub", и JwtUserIdProvider читает именно его.
        Assert.Equal(userId.ToString(), principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value);
    }

    [Fact]
    public void Token_signed_with_another_key_is_rejected()
    {
        var options = ValidOptions();
        var foreign = new JwtOptions
        {
            Secret = "a-completely-different-secret-value-0123456789",
            Issuer = options.Issuer,
            Audience = options.Audience,
            ExpiresMinutes = options.ExpiresMinutes
        };
        var token = Issue(foreign, Guid.NewGuid(), Now);

        Assert.ThrowsAny<SecurityTokenException>(() =>
            Handler().ValidateToken(token, ValidationParameters(options, Now), out _));
    }

    [Fact]
    public void Token_with_foreign_audience_is_rejected()
    {
        var options = ValidOptions();
        var foreign = new JwtOptions
        {
            Secret = options.Secret,
            Issuer = options.Issuer,
            Audience = "someone-else",
            ExpiresMinutes = options.ExpiresMinutes
        };
        var token = Issue(foreign, Guid.NewGuid(), Now);

        Assert.ThrowsAny<SecurityTokenException>(() =>
            Handler().ValidateToken(token, ValidationParameters(options, Now), out _));
    }

    [Fact]
    public void Expired_token_is_rejected()
    {
        var options = ValidOptions();
        var token = Issue(options, Guid.NewGuid(), Now);

        // Смотрим на момент после истечения (срок жизни + запас ClockSkew из параметров Api).
        var later = Now.AddMinutes(options.ExpiresMinutes).AddMinutes(1);

        Assert.ThrowsAny<SecurityTokenException>(() =>
            Handler().ValidateToken(token, ValidationParameters(options, later), out _));
    }

    [Fact]
    public void Token_service_refuses_to_issue_without_valid_secret()
    {
        var broken = new JwtOptions { Secret = "short" };
        var service = new JwtTokenService(Options.Create(broken), new TestTimeProvider(Now));

        Assert.Throws<InvalidOperationException>(() => service.Issue(Guid.NewGuid()));
    }
}
