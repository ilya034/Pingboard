using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pingboard.Api.Infrastructure;
using Pingboard.Infrastructure.Security.Options;

namespace Pingboard.Application.Tests.Auth;

/// <summary>
///     Проверяет, что схема аутентификации настроена так же, как её проверяет выдача токенов:
///     ключ подписи, issuer, audience и отключённый маппинг claims. Ошибка здесь не ломает
///     сборку — она превращается в «все получают 401» или, что хуже, в приём чужих токенов.
/// </summary>
public sealed class JwtBearerSetupTests
{
    private const string Secret = "setup-test-secret-value-0123456789abcdef";

    private static (ServiceProvider Provider, JwtOptions Options) Build()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = Secret,
                ["Jwt:Issuer"] = "pingboard",
                ["Jwt:Audience"] = "pingboard",
                ["Jwt:ExpiresMinutes"] = "120"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddJwtAuthentication(configuration);

        return (services.BuildServiceProvider(), configuration.GetSection("Jwt").Get<JwtOptions>()!);
    }

    [Fact]
    public async Task Jwt_bearer_is_the_scheme_used_by_default()
    {
        var (provider, _) = Build();

        // Источник правды — провайдер схем: именно его спрашивает конвейер, когда
        // решает, чем аутентифицировать запрос. Значение AuthenticationOptions.DefaultScheme
        // в изоляции от WebApplicationBuilder не выставляется, поэтому проверяем то,
        // что реально влияет на обработку запроса.
        var schemeProvider = provider.GetRequiredService<IAuthenticationSchemeProvider>();
        var scheme = await schemeProvider.GetDefaultAuthenticateSchemeAsync();

        Assert.Equal(JwtBearerDefaults.AuthenticationScheme, scheme?.Name);
    }

    [Fact]
    public async Task All_default_schemes_point_at_bearer()
    {
        var (provider, _) = Build();
        var schemeProvider = provider.GetRequiredService<IAuthenticationSchemeProvider>();

        var authenticate = await schemeProvider.GetDefaultAuthenticateSchemeAsync();
        var challenge = await schemeProvider.GetDefaultChallengeSchemeAsync();

        Assert.Equal(JwtBearerDefaults.AuthenticationScheme, authenticate?.Name);
        Assert.Equal(JwtBearerDefaults.AuthenticationScheme, challenge?.Name);
    }

    [Fact]
    public void Bearer_options_use_the_same_key_and_audience_as_token_issuing()
    {
        var (provider, jwt) = Build();

        var bearer = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.Equal(jwt.Issuer, bearer.TokenValidationParameters.ValidIssuer);
        Assert.Equal(jwt.Audience, bearer.TokenValidationParameters.ValidAudience);

        // Ключ проверки обязан совпасть с ключом подписи: иначе JwtTokenService выписывает
        // токены, которые схема тут же отвергает.
        var expected = AuthenticationSetup.TokenValidationParametersFor(jwt).IssuerSigningKey;
        Assert.Equal(expected!.ToString(), bearer.TokenValidationParameters.IssuerSigningKey!.ToString());

        Assert.NotNull(bearer.Events);
    }

    [Fact]
    public void Inbound_claim_mapping_is_disabled_so_sub_stays_sub()
    {
        var (provider, _) = Build();

        var bearer = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        // JwtUserIdProvider читает именно "sub": если включить маппинг, claim превратится
        // в длинный WS-Federation URI и владелец перестанет определяться.
        Assert.False(bearer.MapInboundClaims);
    }
}
