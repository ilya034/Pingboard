using Pingboard.Api.Infrastructure;
using Pingboard.Application.Abstractions;
using Pingboard.Application.Observability;

namespace Pingboard.Api.Endpoints;

/// <summary>
/// Регистрация конечных точек проверки состояния приложения (liveness /healthz, readiness /readyz,
/// версии /api/version и метрик /metrics).
/// Проверка доступности БД осуществляется через абстракцию <see cref="IDatabaseHealthProbe" />.
/// </summary>
public static class HealthEndpoints
{
    /// <summary>
    /// Предыдущее состояние готовности (0 — ready, 1 — degraded) для логирования переходов между состояниями.
    /// </summary>
    private static int _degraded;

    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/healthz", (AppVersionInfo info) =>
                Results.Ok(new { status = "ok", version = info.Version, pod = info.PodName }))
            .WithName("Liveness")
            .AllowAnonymous()
            .WithTags("ops")
            .WithSummary("Liveness: process is alive");

        app.MapGet("/readyz", async (
                IDatabaseHealthProbe database,
                AppVersionInfo info,
                ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                var logger = loggerFactory.CreateLogger("Pingboard.Api.Readiness");

                try
                {
                    if (await database.CanConnectAsync(ct))
                    {
                        LogTransition(logger, ready: true, error: null);
                        return Results.Ok(new
                        {
                            status = "ready",
                            database = "ok",
                            version = info.Version,
                            pod = info.PodName
                        });
                    }

                    LogTransition(logger, ready: false, error: null);
                }
                catch (Exception ex)
                {
                    LogTransition(logger, ready: false, error: ex);
                }

                return Results.Json(
                    new
                    {
                        status = "degraded",
                        database = "unavailable",
                        version = info.Version,
                        pod = info.PodName
                    },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            })
            .WithName("Readiness")
            .AllowAnonymous()
            .WithTags("ops")
            .WithSummary("Readiness: SELECT 1 Postgres");

        app.MapGet("/api/version", (AppVersionInfo info, TimeProvider timeProvider) =>
                Results.Ok(new
                {
                    version = info.Version,
                    pod = info.PodName,
                    timestamp = timeProvider.GetUtcNow()
                }))
            .WithName("Version")
            .AllowAnonymous()
            .WithTags("ops")
            .WithSummary("Application version and pod identity");

        app.MapGet("/metrics", (PrometheusMetricsExporter exporter) =>
                Results.Text(exporter.Scrape(), PrometheusMetricsExporter.ContentType))
            .WithName("Metrics")
            .AllowAnonymous()
            .WithTags("ops")
            .WithSummary("Prometheus metrics (runtime, ASP.NET Core and business metrics)");

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
