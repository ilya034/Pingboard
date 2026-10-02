using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Pingboard.Api.Infrastructure;

/// <summary>
///     Единый маппинг исключений в ответы RFC 7807 ProblemDetails.
///     Тела ошибок структурные — это часть SRE-обвязки из §10 PLAN.md.
/// </summary>
public static class ErrorHandling
{
    public static IApplicationBuilder UseApiErrorHandling(this IApplicationBuilder app)
    {
        return app.UseExceptionHandler(handler => handler.Run(async context =>
        {
            var feature = context.Features.Get<IExceptionHandlerFeature>();
            var exception = feature?.Error;

            var mapped = ExceptionStatusMapper.Map(exception);

            // 5xx пишем в лог: 503 (недоступная БД) — тоже, потому что это повод для алерта.
            if (mapped.Status >= StatusCodes.Status500InternalServerError)
                context.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Pingboard.Api.Unhandled")
                    .LogError(exception, "Error {Status} on {Path}", mapped.Status, context.Request.Path);

            // 401 без этого заголовка не соответствует RFC 9110/6750, и клиенты
            // (в том числе Swagger/Scalar) не понимают, какой схеме аутентифицироваться.
            if (mapped.WwwAuthenticate is not null)
                context.Response.Headers.WWWAuthenticate = mapped.WwwAuthenticate;

            var problem = new ProblemDetails
            {
                Status = mapped.Status,
                Title = mapped.Title,
                // Для 5xx наружу уходит общая формулировка: детали — в логе, не в ответе.
                Detail = mapped.Detail,
                Instance = context.Request.Path
            };

            problem.Extensions["traceId"] = context.TraceIdentifier;

            if (mapped.Errors is not null) problem.Extensions["errors"] = mapped.Errors;

            context.Response.StatusCode = mapped.Status;

            await ErrorResponseFormat.WriteAsync(context, problem);
        }));
    }
}
