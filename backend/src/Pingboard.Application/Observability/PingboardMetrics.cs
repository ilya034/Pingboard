using System.Diagnostics.Metrics;
using Pingboard.Application.Abstractions;

namespace Pingboard.Application.Observability;

/// <summary>
///     Реализация бизнес-метрик доменной области Pingboard на базе штатного <see cref="Meter" /> (.NET Diagnostics)
///     и встроенного экспортера в формат Prometheus.
/// </summary>
public sealed class PingboardMetrics : IPingboardMetrics, IDisposable
{
    public const string MeterName = "Pingboard";

    /// <summary>Экземпляр по умолчанию для использования вне DI (например, в модульных тестах).</summary>
    public static readonly PingboardMetrics Default = new(PrometheusMetricsExporter.Default);

    private readonly PrometheusMetricsExporter _exporter;
    private readonly Meter _meter;

    private readonly Counter<long> _usersRegistered;
    private readonly Counter<long> _authLogins;
    private readonly Counter<long> _monitorsCreated;
    private readonly Counter<long> _monitorsDeleted;
    private readonly Counter<long> _probeChecks;
    private readonly Histogram<double> _probeDuration;
    private readonly Counter<long> _workerIterations;

    public PingboardMetrics(PrometheusMetricsExporter exporter)
    {
        _exporter = exporter;
        _meter = new Meter(MeterName, "0.1.0");

        _usersRegistered = _meter.CreateCounter<long>(
            "pingboard_users_registered_total",
            description: "Total number of successfully registered users");

        _authLogins = _meter.CreateCounter<long>(
            "pingboard_auth_logins_total",
            description: "Total number of user login attempts by result");

        _monitorsCreated = _meter.CreateCounter<long>(
            "pingboard_monitors_created_total",
            description: "Total number of monitors created by users");

        _monitorsDeleted = _meter.CreateCounter<long>(
            "pingboard_monitors_deleted_total",
            description: "Total number of monitors deleted by users");

        _probeChecks = _meter.CreateCounter<long>(
            "pingboard_probe_checks_total",
            description: "Total number of executed monitor availability checks by status");

        _probeDuration = _meter.CreateHistogram<double>(
            "pingboard_probe_duration_seconds",
            unit: "s",
            description: "Duration of monitor availability HTTP checks in seconds");

        _workerIterations = _meter.CreateCounter<long>(
            "pingboard_worker_iterations_total",
            description: "Total number of background worker check loop iterations by result");

        // Предварительная инициализация временных рядов нулевыми значениями,
        // чтобы все бизнес-метрики сразу присутствовали в выдаче /metrics при старте сервиса.
        _exporter.InitializeBusinessMetrics();
    }

    public void RecordUserRegistered()
    {
        _usersRegistered.Add(1);
        _exporter.IncrementCounter(
            "pingboard_users_registered_total",
            "Total number of successfully registered users",
            labels: string.Empty);
    }

    public void RecordLoginAttempt(bool success)
    {
        var result = success ? "success" : "failure";
        _authLogins.Add(1, new KeyValuePair<string, object?>("result", result));
        _exporter.IncrementCounter(
            "pingboard_auth_logins_total",
            "Total number of user login attempts by result",
            labels: $"result=\"{result}\"");
    }

    public void RecordMonitorCreated()
    {
        _monitorsCreated.Add(1);
        _exporter.IncrementCounter(
            "pingboard_monitors_created_total",
            "Total number of monitors created by users",
            labels: string.Empty);
    }

    public void RecordMonitorDeleted()
    {
        _monitorsDeleted.Add(1);
        _exporter.IncrementCounter(
            "pingboard_monitors_deleted_total",
            "Total number of monitors deleted by users",
            labels: string.Empty);
    }

    public void RecordProbeCheck(bool up, double durationSeconds)
    {
        var status = up ? "up" : "down";
        _probeChecks.Add(1, new KeyValuePair<string, object?>("status", status));
        _exporter.IncrementCounter(
            "pingboard_probe_checks_total",
            "Total number of executed monitor availability checks by status",
            labels: $"status=\"{status}\"");

        var clampedDuration = Math.Max(0.0, durationSeconds);
        _probeDuration.Record(clampedDuration, new KeyValuePair<string, object?>("status", status));
        _exporter.ObserveHistogram(
            "pingboard_probe_duration_seconds",
            "Duration of monitor availability HTTP checks in seconds",
            labels: $"status=\"{status}\"",
            value: clampedDuration);
    }

    public void RecordWorkerIteration(bool success)
    {
        var result = success ? "success" : "failure";
        _workerIterations.Add(1, new KeyValuePair<string, object?>("result", result));
        _exporter.IncrementCounter(
            "pingboard_worker_iterations_total",
            "Total number of background worker check loop iterations by result",
            labels: $"result=\"{result}\"");
    }

    public void Dispose()
    {
        _meter.Dispose();
    }
}
