using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Pingboard.Api.Infrastructure;
using Pingboard.Application.Common;

namespace Pingboard.Application.Tests.Auth;

/// <summary>
///     Владелец запроса обязан приходить из токена. Раньше ICurrentUser возвращал
///     фиксированного демо-пользователя из конфига, то есть валидный токен любого
///     аккаунта давал доступ к чужим мониторам — эти тесты фиксируют, что такой
///     «владелец по умолчанию» не вернётся.
/// </summary>
public sealed class JwtUserIdProviderTests
{
    private static JwtUserIdProvider ProviderFor(ClaimsPrincipal? principal)
    {
        var accessor = new HttpContextAccessor();

        if (principal is not null)
        {
            accessor.HttpContext = new DefaultHttpContext { User = principal };
        }

        return new JwtUserIdProvider(accessor);
    }

    private static ClaimsPrincipal PrincipalWithSub(string value)
    {
        return new ClaimsPrincipal(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, value)], "Bearer"));
    }

    [Fact]
    public void Returns_user_id_from_sub_claim()
    {
        var userId = Guid.NewGuid();
        var provider = ProviderFor(PrincipalWithSub(userId.ToString()));

        Assert.Equal(userId, provider.UserId);
    }

    [Fact]
    public void Throws_unauthorized_when_sub_claim_is_missing()
    {
        var provider = ProviderFor(new ClaimsPrincipal(new ClaimsIdentity([], "Bearer")));

        Assert.Throws<UnauthorizedException>(() => provider.UserId);
    }

    [Fact]
    public void Throws_unauthorized_when_sub_claim_is_not_a_guid()
    {
        var provider = ProviderFor(PrincipalWithSub("not-a-guid"));

        Assert.Throws<UnauthorizedException>(() => provider.UserId);
    }

    [Fact]
    public void Throws_unauthorized_when_there_is_no_http_context()
    {
        var provider = ProviderFor(null);

        Assert.Throws<UnauthorizedException>(() => provider.UserId);
    }

    [Fact]
    public void Does_not_fall_back_to_any_default_user()
    {
        // Ключевой инвариант: без claim sub доступа нет — никакого «демо-пользователя по умолчанию».
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var provider = new JwtUserIdProvider(accessor);

        Assert.Throws<UnauthorizedException>(() => provider.UserId);
    }
}
