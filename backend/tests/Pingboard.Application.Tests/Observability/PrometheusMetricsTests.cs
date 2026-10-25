using Pingboard.Application.Observability;
using Xunit;

namespace Pingboard.Application.Tests.Observability;

public sealed class PrometheusMetricsTests
{
    [Fact]
    public void Scrape_IncludesInitializedBusinessAndRuntimeMetrics()
    {
        var exporter = new PrometheusMetricsExporter();
        using var metrics = new PingboardMetrics(exporter);

        var output = exporter.Scrape();

        // Метрики рантайма .NET / процесса
        Assert.Contains("# TYPE process_cpu_seconds_total counter", output);
        Assert.Contains("# TYPE process_working_set_bytes gauge", output);
        Assert.Contains("# TYPE dotnet_total_memory_bytes gauge", output);
        Assert.Contains("# TYPE dotnet_gc_collection_count_total counter", output);
        Assert.Contains("dotnet_gc_collection_count_total{generation=\"0\"}", output);
        Assert.Contains("# TYPE dotnet_threadpool_threads_total gauge", output);

        // Бизнес-метрики доменной области
        Assert.Contains("pingboard_users_registered_total 0", output);
        Assert.Contains("pingboard_auth_logins_total{result=\"success\"} 0", output);
        Assert.Contains("pingboard_auth_logins_total{result=\"failure\"} 0", output);
        Assert.Contains("pingboard_monitors_created_total 0", output);
        Assert.Contains("pingboard_monitors_deleted_total 0", output);
        Assert.Contains("pingboard_probe_checks_total{status=\"up\"} 0", output);
        Assert.Contains("pingboard_probe_checks_total{status=\"down\"} 0", output);
    }

    [Fact]
    public void RecordBusinessEvents_UpdatesCountersAndHistogramInPrometheusFormat()
    {
        var exporter = new PrometheusMetricsExporter();
        using var metrics = new PingboardMetrics(exporter);

        metrics.RecordUserRegistered();
        metrics.RecordLoginAttempt(success: true);
        metrics.RecordLoginAttempt(success: true);
        metrics.RecordLoginAttempt(success: false);
        metrics.RecordMonitorCreated();
        metrics.RecordMonitorCreated();
        metrics.RecordMonitorDeleted();
        metrics.RecordProbeCheck(up: true, durationSeconds: 0.042);
        metrics.RecordProbeCheck(up: false, durationSeconds: 1.5);
        metrics.RecordWorkerIteration(success: true);

        var output = exporter.Scrape();

        Assert.Contains("pingboard_users_registered_total 1", output);
        Assert.Contains("pingboard_auth_logins_total{result=\"success\"} 2", output);
        Assert.Contains("pingboard_auth_logins_total{result=\"failure\"} 1", output);
        Assert.Contains("pingboard_monitors_created_total 2", output);
        Assert.Contains("pingboard_monitors_deleted_total 1", output);
        Assert.Contains("pingboard_probe_checks_total{status=\"up\"} 1", output);
        Assert.Contains("pingboard_probe_checks_total{status=\"down\"} 1", output);
        Assert.Contains("pingboard_probe_duration_seconds_count{status=\"up\"} 1", output);
        Assert.Contains("pingboard_probe_duration_seconds_count{status=\"down\"} 1", output);
        Assert.Contains("pingboard_worker_iterations_total{result=\"success\"} 1", output);
    }
}
