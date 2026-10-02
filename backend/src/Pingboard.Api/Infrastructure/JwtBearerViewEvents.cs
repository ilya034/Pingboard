using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;

namespace Pingboard.Api.Infrastructure;

/// <summary>
///     Ответы схемы аутентификации: без этого 401/403 от JwtBearer приходят с пустым телом,
///     а весь остальной API отвечает RFC 7807 ProblemDetails. Единый формат ошибки —
///     часть SRE-обвязки из §10 PLAN.md, и клиенту не приходится разбирать два разных.
/// </summary>
internal static class JwtBearerViewEvents
{
    public static JwtBearerEvents Create()
    {
        return new JwtBearerEvents
        {
            // Нет токена или он не прошёл проверку (истёк, чужая подпись, не тот audience).
            OnChallenge = async context =>
            {
                context.HandleResponse();

                await WriteProblemAsync(
                    context.HttpContext,
                    StatusCodes.Status401Unauthorized,
                    "Требуется аутентификация.",
                    "Передайте валидный access-токен в заголовке Authorization: Bearer <token>.",
                    challenge: ErrorResponseFormat.BearerChallenge);
            },

            // Токен валиден, но прав на операцию не хватает.
            OnForbidden = async context =>
            {
                await WriteProblemAsync(
                    context.HttpContext,
                    StatusCodes.Status403Forbidden,
                    "Доступ запрещён.",
                    "Токен валиден, но прав на эту операцию нет.");
            }
        };
    }

    private static Task WriteProblemAsync(HttpContext http, int status, string title, string detail, string? challenge = null)
    {
        http.Response.StatusCode = status;

        // Заголовок ставится до записи тела: после старта ответа менять заголовки поздно.
        if (challenge is not null) http.Response.Headers.WWWAuthenticate = challenge;

        return ErrorResponseFormat.WriteAsync(http, new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = http.Request.Path,
            Extensions = { ["traceId"] = http.TraceIdentifier }
        });
    }
}
