using Pingboard.Api.Infrastructure;
using Pingboard.Application.Auth;
using Pingboard.Application.Auth.Dtos;

namespace Pingboard.Api.Endpoints;

/// <summary>
///     Регистрация/логин. Оба маршрута анонимные (владельцем пользователь становится
///     по claim sub выданного токена), поэтому у каждого свой rate limit: каждый запрос
///     стоит PBKDF2 с 210k итераций. Лимиты разные — регистрация это ещё и запись в БД,
///     поэтому она строже логина (см. <see cref="AuthRateLimiting" />).
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth")
            .WithTags("auth");

        // 201 без Location: отдельного ресурса пользователя в API нет, а ссылка на
        // /api/auth/login (другой POST-маршрут) нарушала бы RFC 9110 — Location обязан
        // указывать на созданный ресурс.
        group.MapPost("/register", async (RegisterRequest request, RegisterUser useCase, CancellationToken ct) =>
                Results.Json(await useCase.ExecuteAsync(request, ct), statusCode: StatusCodes.Status201Created))
            .WithName("Register")
            .AllowAnonymous()
            .RequireRateLimiting(AuthRateLimiting.RegisterPolicyName)
            .WithSummary("Создать аккаунт и получить access-токен");

        group.MapPost("/login", async (LoginRequest request, LoginUser useCase, CancellationToken ct) =>
                Results.Ok(await useCase.ExecuteAsync(request, ct)))
            .WithName("Login")
            .AllowAnonymous()
            .RequireRateLimiting(AuthRateLimiting.LoginPolicyName)
            .WithSummary("Войти и получить access-токен");

        return app;
    }
}
