using Microsoft.Extensions.Configuration;
using Pingboard.Application.Observability;

namespace Pingboard.Worker.Hosting;

public sealed class WorkerChaosState
{
    private double _failureRate;
    private readonly PrometheusMetricsExporter _exporter;

    public WorkerChaosState(IConfiguration configuration, PrometheusMetricsExporter exporter)
    {
        _exporter = exporter;
        var initial = double.TryParse(
            configuration["Chaos:WorkerFailureRate"],
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : 0.0;

        SetFailureRate(initial);
    }

    public double FailureRate => Volatile.Read(ref _failureRate);

    public void SetFailureRate(double rate)
    {
        var clamped = Math.Clamp(rate, 0.0, 1.0);
        Volatile.Write(ref _failureRate, clamped);

        _exporter.SetGauge(
            "pingboard_worker_chaos_failure_rate_percent",
            "Configured artificial worker iteration failure rate in percent (0..100).",
            labels: string.Empty,
            value: (long)Math.Round(clamped * 100.0));
    }
}
