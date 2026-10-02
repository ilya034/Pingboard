using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Pingboard.Infrastructure.Security.Options;

namespace Pingboard.Api.Infrastructure;

/// <summary>
///     JWT Bearer включается в M4: маршруты мониторов требуют access-токен,
///     владелец берётся из claim <c>sub</c> (JwtUserIdProvider).
/// </summary>
public static class AuthenticationSetup
{
    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services,
        IConfiguration configuration)
    {
        // Биндинг и проверки секции Jwt живут в Infrastructure (рядом с JwtTokenService);
        // здесь добавляется только fail-fast: стартовать с кривым Jwt__Secret нельзя.
        services.AddOptions<JwtOptions>().ValidateOnStart();

        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Claim "sub" остаётся самим собой: не подменяем его на длинный
                // WS-Federation URI, чтобы имя claim в токене и в коде совпадало.
                options.MapInboundClaims = false;
                options.TokenValidationParameters = TokenValidationParametersFor(jwt);
                options.Events = JwtBearerViewEvents.Create();
            });

        services.AddAuthorization();

        return services;
    }

    /// <summary>
    ///     Ровно те параметры, которыми Api проверяет токены. Вынесено отдельно,
    ///     чтобы тесты могли проверить связку «JwtTokenService выписал → схема приняла»
    ///     на настоящих настройках, а не на своей копии.
    /// </summary>
    public static TokenValidationParameters TokenValidationParametersFor(JwtOptions jwt)
    {
        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)),
            // Небольшой запас на расхождение часов отдающего и проверяющего.
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    }
}
