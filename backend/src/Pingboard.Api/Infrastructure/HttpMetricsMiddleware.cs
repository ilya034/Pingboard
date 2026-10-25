using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.Routing;
using Pingboard.Application.Observability;

namespace Pingboard.Api.Infrastructure;

/// <summary>
///     Middleware для сбора HTTP-метрик фреймворка ASP.NET Core:
///     количество активных запросов, общее число запросов и гистограмма времени ответа.
/// </summary>
public static class HttpMetricsMiddleware
{
    public static IApplicationBuilder UseHttpMetrics(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            var exporter = context.RequestServices.GetService<PrometheusMetricsExporter>()
                           ?? PrometheusMetricsExporter.Default;

            var method = SanitizeLabel(context.Request.Method);
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
                var labels = $"method=\"{method}\",route=\"{route}\",status_code=\"{statusCode}\"";

                exporter.IncrementCounter(
                    "http_requests_received_total",
                    "Total number of HTTP requests received by the ASP.NET Core pipeline.",
                    labels);

                exporter.ObserveHistogram(
                    "http_request_duration_seconds",
                    "Duration of HTTP requests processed by the ASP.NET Core pipeline in seconds.",
                    labels,
                    elapsedSeconds);
            }
        });
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
