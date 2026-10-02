using Pingboard.Application.Abstractions;

namespace Pingboard.Api.Endpoints;

/// <summary>
///     Liveness и readiness(§10 PLAN.md):
///     /healthz — процесс жив, оркестратор не перезапускает контейнер;
///     /readyz  — сервис готов принимать трафик, то есть БД доступна.
///     БД проверяется через порт <see cref="IDatabaseHealthProbe" />: в Api нет типов EF Core (§2 README).
/// </summary>
public static class HealthEndpoints
{
    /// <summary>Последнее доложенное состояние: 0 — ready, 1 — degraded. См. <see cref="LogTransition" />.</summary>
    private static int _degraded;

    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }))
            .WithName("Liveness")
            .AllowAnonymous()
            .WithTags("ops")
            .WithSummary("Liveness: process is alive");

        app.MapGet("/readyz", async (IDatabaseHealthProbe database, ILoggerFactory loggerFactory, CancellationToken ct) =>
            {
                var logger = loggerFactory.CreateLogger("Pingboard.Api.Readiness");

                try
                {
                    if (await database.CanConnectAsync(ct))
                    {
                        LogTransition(logger, ready: true, error: null);
                        return Results.Ok(new { status = "ready", database = "ok" });
                    }

                    LogTransition(logger, ready: false, error: null);
                }
                catch (Exception ex)
                {
                    LogTransition(logger, ready: false, error: ex);
                }

                return Results.Json(new { status = "degraded", database = "unavailable" },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            })
            .WithName("Readiness")
            .AllowAnonymous()
            .WithTags("ops")
            .WithSummary("Readiness: SELECT 1 Postgres");

        // TODO: /metrics — prometheus-net.AspNetCore. Пакет не в оффлайн-фиде,
        // поэтому в каркасе маршрута нет;
        //   app.MapMetrics();  + services.AddSingleton<IMetricsRoot>(Metrics.Default);
        return app;
    }
    
    private static void LogTransition(ILogger logger, bool ready, Exception? error)
    {
        var wasDegraded = Interlocked.Exchange(ref _degraded, ready ? 0 : 1) == 1;

        if (ready)
        {
            if (wasDegraded)
                logger.LogInformation("Readiness restored: DB is available again");
            return;
        }

        if (!wasDegraded)
            logger.LogWarning(error, "Readiness check failed: DB is unavailable");
    }
}
