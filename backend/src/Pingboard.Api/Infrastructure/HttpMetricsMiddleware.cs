using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.Routing;
using Pingboard.Application.Observability;

namespace Pingboard.Api.Infrastructure;

/// <summary>
///     Middleware для сбора HTTP-метрик фреймворка ASP.NET Core, простановки заголовков
///     версии/пода (X-App-Version, X-Pod-Name) и структурированного логирования пользовательских запросов.
/// </summary>
public static class HttpMetricsMiddleware
{
    public static void RegisterBuildInfoMetric(this PrometheusMetricsExporter exporter, AppVersionInfo versionInfo)
    {
        var version = SanitizeLabel(versionInfo.Version);
        var pod = SanitizeLabel(versionInfo.PodName);
        exporter.SetGauge(
            "pingboard_build_info",
            "Build and runtime identity metadata of the Pingboard API instance.",
            $"version=\"{version}\",pod=\"{pod}\"",
            value: 1);
    }

    public static IApplicationBuilder UseHttpMetrics(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            var exporter = context.RequestServices.GetService<PrometheusMetricsExporter>()
                           ?? PrometheusMetricsExporter.Default;
            var versionInfo = context.RequestServices.GetService<AppVersionInfo>()
                              ?? new AppVersionInfo("dev", Environment.MachineName);

            context.Response.Headers[AppVersionInfo.VersionHeaderName] = versionInfo.Version;
            context.Response.Headers[AppVersionInfo.PodHeaderName] = versionInfo.PodName;

            var method = SanitizeLabel(context.Request.Method);
            var version = SanitizeLabel(versionInfo.Version);
            var methodLabel = $"method=\"{method}\"";

            exporter.AddGauge(
                "http_requests_in_progress",
                "Number of HTTP requests currently being processed by the ASP.NET Core pipeline.",
                methodLabel,
                delta: 1);

            var startTimestamp = Stopwatch.GetTimestamp();

            try
            {
                await next(context);
            }
            finally
            {
                var elapsedSeconds = Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds;
                exporter.AddGauge(
                    "http_requests_in_progress",
                    "Number of HTTP requests currently being processed by the ASP.NET Core pipeline.",
                    methodLabel,
                    delta: -1);

                var route = ResolveRouteLabel(context);
                var statusCode = context.Response.StatusCode.ToString(CultureInfo.InvariantCulture);
                var labels = $"method=\"{method}\",route=\"{route}\",status_code=\"{statusCode}\",version=\"{version}\"";

                exporter.IncrementCounter(
                    "http_requests_received_total",
                    "Total number of HTTP requests received by the ASP.NET Core pipeline.",
                    labels);

                exporter.ObserveHistogram(
                    "http_request_duration_seconds",
                    "Duration of HTTP requests processed by the ASP.NET Core pipeline in seconds.",
                    labels,
                    elapsedSeconds);

                LogIfUserRequest(context, versionInfo, elapsedSeconds);
            }
        });
    }

    private static void LogIfUserRequest(HttpContext context, AppVersionInfo versionInfo, double elapsedSeconds)
    {
        var path = context.Request.Path.Value ?? "/";
        if (path.Equals("/healthz", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/readyz", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/metrics", StringComparison.OrdinalIgnoreCase))
            return;

        var logger = context.RequestServices
            .GetService<ILoggerFactory>()?
            .CreateLogger("Pingboard.Api.HttpRequest");

        if (logger is null || !logger.IsEnabled(LogLevel.Information))
            return;

        var userGroup = context.Request.Headers["x-user-group"].FirstOrDefault() ?? "default";
        var requestId = context.Request.Headers["X-Request-Id"].FirstOrDefault() ?? context.TraceIdentifier;

        logger.LogInformation(
            "HTTP {Method} {Path} responded {StatusCode} in {DurationMs:F2} ms (version={Version}, pod={Pod}, userGroup={UserGroup}, requestId={RequestId})",
            context.Request.Method,
            path,
            context.Response.StatusCode,
            elapsedSeconds * 1000.0,
            versionInfo.Version,
            versionInfo.PodName,
            userGroup,
            requestId);
    }

    private static string ResolveRouteLabel(HttpContext context)
    {
        if (context.GetEndpoint() is RouteEndpoint routeEndpoint &&
            !string.IsNullOrWhiteSpace(routeEndpoint.RoutePattern.RawText))
        {
            var raw = routeEndpoint.RoutePattern.RawText!;
            return SanitizeLabel(raw.StartsWith('/') ? raw : "/" + raw);
        }

        var path = context.Request.Path.Value;
        if (string.IsNullOrWhiteSpace(path))
            return "/";

        return SanitizeLabel(path);
    }

    private static string SanitizeLabel(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("\"", "\\\"", StringComparison.Ordinal);
}
