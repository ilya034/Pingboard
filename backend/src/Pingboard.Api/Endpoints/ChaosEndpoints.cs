using System.Globalization;
using Pingboard.Api.Infrastructure;

namespace Pingboard.Api.Endpoints;

public static class ChaosEndpoints
{
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(3)
    };

    public static IEndpointRouteBuilder MapChaosEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/ops/chaos", (ChaosSimulationState state) =>
                Results.Ok(new ChaosStatusResponse(
                    ErrorRate: state.ErrorRate,
                    LatencyMs: state.LatencyMs,
                    WorkerFailureRate: state.WorkerFailureRate,
                    Active: state.ErrorRate > 0.0 || state.LatencyMs > 0 || state.WorkerFailureRate > 0.0)))
            .WithName("GetChaosStatus")
            .AllowAnonymous()
            .WithTags("ops")
            .WithSummary("Get current fault injection (chaos) parameters");

        app.MapPost("/ops/chaos", async (
                ChaosUpdateRequest request,
                ChaosSimulationState state,
                IConfiguration configuration,
                ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                var logger = loggerFactory.CreateLogger("Pingboard.Api.Chaos");
                string? workerSyncResult = null;

                if (request.WorkerFailureRate.HasValue)
                {
                    var workerControlUrl = configuration["Chaos:WorkerControlUrl"] ?? "http://worker:9100";
                    var clampedWorkerRate = Math.Clamp(request.WorkerFailureRate.Value, 0.0, 1.0);
                    var targetUri =
                        $"{workerControlUrl.TrimEnd('/')}/chaos?failureRate={clampedWorkerRate.ToString("G", CultureInfo.InvariantCulture)}";

                    try
                    {
                        using var response = await SharedHttpClient.PostAsync(targetUri, content: null, ct);
                        workerSyncResult = response.IsSuccessStatusCode
                            ? "synced"
                            : $"http_{(int)response.StatusCode}";
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Could not forward workerFailureRate to {Uri}", targetUri);
                        workerSyncResult = "unreachable";
                    }
                }

                state.Update(request.ErrorRate, request.LatencyMs, request.WorkerFailureRate);

                logger.LogWarning(
                    "Chaos simulation updated: errorRate={ErrorRate}, latencyMs={LatencyMs}, workerFailureRate={WorkerFailureRate} ({WorkerSync})",
                    state.ErrorRate,
                    state.LatencyMs,
                    state.WorkerFailureRate,
                    workerSyncResult ?? "not_requested");

                return Results.Ok(new ChaosStatusResponse(
                    ErrorRate: state.ErrorRate,
                    LatencyMs: state.LatencyMs,
                    WorkerFailureRate: state.WorkerFailureRate,
                    Active: state.ErrorRate > 0.0 || state.LatencyMs > 0 || state.WorkerFailureRate > 0.0,
                    WorkerSync: workerSyncResult));
            })
            .WithName("UpdateChaosStatus")
            .AllowAnonymous()
            .WithTags("ops")
            .WithSummary("Update fault injection (chaos) parameters for API and Worker");

        return app;
    }

    public sealed record ChaosUpdateRequest(
        double? ErrorRate,
        int? LatencyMs,
        double? WorkerFailureRate);

    public sealed record ChaosStatusResponse(
        double ErrorRate,
        int LatencyMs,
        double WorkerFailureRate,
        bool Active,
        string? WorkerSync = null);
}
