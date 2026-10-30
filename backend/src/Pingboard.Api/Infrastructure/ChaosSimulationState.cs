using Pingboard.Application.Observability;

namespace Pingboard.Api.Infrastructure;

public sealed class ChaosSimulationState
{
    private double _errorRate;
    private int _latencyMs;
    private double _workerFailureRate;
    private readonly PrometheusMetricsExporter _exporter;

    public ChaosSimulationState(IConfiguration configuration, PrometheusMetricsExporter exporter)
    {
        _exporter = exporter;

        var initialErrorRate = Math.Clamp(configuration.GetValue("Chaos:ErrorRate", 0.0), 0.0, 1.0);
        var initialLatencyMs = Math.Clamp(configuration.GetValue("Chaos:LatencyMs", 0), 0, 10_000);
        var initialWorkerFailureRate = Math.Clamp(configuration.GetValue("Chaos:WorkerFailureRate", 0.0), 0.0, 1.0);

        Update(initialErrorRate, initialLatencyMs, initialWorkerFailureRate);
    }

    public double ErrorRate => Volatile.Read(ref _errorRate);

    public int LatencyMs => Volatile.Read(ref _latencyMs);

    public double WorkerFailureRate => Volatile.Read(ref _workerFailureRate);

    public void Update(double? errorRate, int? latencyMs, double? workerFailureRate = null)
    {
        var nextErrorRate = errorRate.HasValue
            ? Math.Clamp(errorRate.Value, 0.0, 1.0)
            : ErrorRate;

        var nextLatencyMs = latencyMs.HasValue
            ? Math.Clamp(latencyMs.Value, 0, 10_000)
            : LatencyMs;

        var nextWorkerRate = workerFailureRate.HasValue
            ? Math.Clamp(workerFailureRate.Value, 0.0, 1.0)
            : WorkerFailureRate;

        Volatile.Write(ref _errorRate, nextErrorRate);
        Volatile.Write(ref _latencyMs, nextLatencyMs);
        Volatile.Write(ref _workerFailureRate, nextWorkerRate);

        var active = nextErrorRate > 0.0 || nextLatencyMs > 0 || nextWorkerRate > 0.0 ? 1L : 0L;

        _exporter.SetGauge(
            "pingboard_chaos_active",
            "Indicates whether fault injection (chaos simulation) mode is currently active (1 = active, 0 = normal).",
            labels: string.Empty,
            value: active);

        _exporter.SetGauge(
            "pingboard_chaos_latency_ms",
            "Configured artificial HTTP API latency in milliseconds.",
            labels: string.Empty,
            value: nextLatencyMs);

        _exporter.SetGauge(
            "pingboard_chaos_error_rate_percent",
            "Configured artificial HTTP 500 error rate in percent (0..100).",
            labels: string.Empty,
            value: (long)Math.Round(nextErrorRate * 100.0));
    }
}
