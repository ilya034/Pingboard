using Pingboard.Api.Infrastructure;
using Pingboard.Application.Auth;
using Pingboard.Application.Auth.Dtos;

namespace Pingboard.Api.Endpoints;

/// <summary>
/// Регистрация конечных точек аутентификации (регистрация и вход пользователей).
/// Маршруты общедоступны и защищены раздельными политиками ограничения частоты запросов.
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth")
            .WithTags("auth");

        // Возврат 201 без заголовка Location: ресурс пользователя для прямого чтения через GET отсутствует.
        group.MapPost("/register", async (RegisterRequest request, RegisterUser useCase, CancellationToken ct) =>
                Results.Json(await useCase.ExecuteAsync(request, ct), statusCode: StatusCodes.Status201Created))
            .WithName("Register")
            .AllowAnonymous()
            .RequireRateLimiting(AuthRateLimiting.RegisterPolicyName)
            .WithSummary("Create a new user and get access token");

        group.MapPost("/login", async (LoginRequest request, LoginUser useCase, CancellationToken ct) =>
                Results.Ok(await useCase.ExecuteAsync(request, ct)))
            .WithName("Login")
            .AllowAnonymous()
            .RequireRateLimiting(AuthRateLimiting.LoginPolicyName)
            .WithSummary("Login and get access token");

        return app;
    }
}
