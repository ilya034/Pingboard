using Microsoft.AspNetCore.Mvc;

namespace Pingboard.Api.Infrastructure;

public static class ChaosSimulationMiddleware
{
    public static IApplicationBuilder UseChaosSimulation(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value ?? string.Empty;
            if (!path.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
            {
                await next(context);
                return;
            }

            var state = context.RequestServices.GetService<ChaosSimulationState>();
            if (state is null)
            {
                await next(context);
                return;
            }

            var latencyMs = state.LatencyMs;
            if (latencyMs > 0)
                await Task.Delay(latencyMs, context.RequestAborted);

            var errorRate = state.ErrorRate;
            if (errorRate > 0.0 && Random.Shared.NextDouble() < errorRate)
            {
                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Title = "Simulated internal server error.",
                    Detail = "Fault injection (chaos simulation) rejected this API request with HTTP 500.",
                    Instance = context.Request.Path
                };
                problem.Extensions["traceId"] = context.TraceIdentifier;

                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await ErrorResponseFormat.WriteAsync(context, problem);
                return;
            }

            await next(context);
        });
    }
}
