using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Text;

namespace Pingboard.Application.Observability;

/// <summary>
///     Потокобезопасный реестр и экспортер метрик рантайма .NET CLR, фреймворка ASP.NET Core
/// </summary>
public sealed class PrometheusMetricsExporter
{
    public const string ContentType = "text/plain; version=0.0.4; charset=utf-8";

    private static readonly double[] DefaultBuckets =
        [0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1.0, 2.5, 5.0, 10.0];

    public static readonly PrometheusMetricsExporter Default = new();

    private readonly ConcurrentDictionary<string, CounterFamily> _counters = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, GaugeFamily> _gauges = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, HistogramFamily> _histograms = new(StringComparer.Ordinal);

    public PrometheusMetricsExporter()
    {
        InitializeBusinessMetrics();
    }

    /// <summary>
    ///     Регистрирует начальные нулевые значения всех бизнес-метрик, чтобы они были доступны
    ///     для скрейпинга сразу после запуска приложения.
    /// </summary>
    public void InitializeBusinessMetrics()
    {
        IncrementCounter(
            "pingboard_users_registered_total",
            "Total number of successfully registered users",
            labels: string.Empty,
            delta: 0);

        IncrementCounter(
            "pingboard_auth_logins_total",
            "Total number of user login attempts by result",
            labels: "result=\"success\"",
            delta: 0);
        IncrementCounter(
            "pingboard_auth_logins_total",
            "Total number of user login attempts by result",
            labels: "result=\"failure\"",
            delta: 0);

        IncrementCounter(
            "pingboard_monitors_created_total",
            "Total number of monitors created by users",
            labels: string.Empty,
            delta: 0);

        IncrementCounter(
            "pingboard_monitors_deleted_total",
            "Total number of monitors deleted by users",
            labels: string.Empty,
            delta: 0);

        IncrementCounter(
            "pingboard_probe_checks_total",
            "Total number of executed monitor availability checks by status",
            labels: "status=\"up\"",
            delta: 0);
        IncrementCounter(
            "pingboard_probe_checks_total",
            "Total number of executed monitor availability checks by status",
            labels: "status=\"down\"",
            delta: 0);

        EnsureHistogram(
            "pingboard_probe_duration_seconds",
            "Duration of monitor availability HTTP checks in seconds",
            labels: "status=\"up\"");
        EnsureHistogram(
            "pingboard_probe_duration_seconds",
            "Duration of monitor availability HTTP checks in seconds",
            labels: "status=\"down\"");

        IncrementCounter(
            "pingboard_worker_iterations_total",
            "Total number of background worker check loop iterations by result",
            labels: "result=\"success\"",
            delta: 0);
        IncrementCounter(
            "pingboard_worker_iterations_total",
            "Total number of background worker check loop iterations by result",
            labels: "result=\"failure\"",
            delta: 0);
    }

    public void IncrementCounter(string name, string help, string labels, long delta = 1)
    {
        var family = _counters.GetOrAdd(name, static (_, h) => new CounterFamily(h), help);
        family.Add(labels, delta);
    }

    public void AddGauge(string name, string help, string labels, long delta)
    {
        var family = _gauges.GetOrAdd(name, static (_, h) => new GaugeFamily(h), help);
        family.Add(labels, delta);
    }

    public void ObserveHistogram(string name, string help, string labels, double value)
    {
        var family = _histograms.GetOrAdd(name, static (_, h) => new HistogramFamily(h, DefaultBuckets), help);
        family.Observe(labels, value);
    }

    public void EnsureHistogram(string name, string help, string labels)
    {
        var family = _histograms.GetOrAdd(name, static (_, h) => new HistogramFamily(h, DefaultBuckets), help);
        family.EnsureSeries(labels);
    }

    /// <summary>
    ///     Формирует полный снимок метрик рантайма .NET, фреймворка и бизнес-метрик в формате Prometheus.
    /// </summary>
    public string Scrape()
    {
        var sb = new StringBuilder(8192);

        WriteRuntimeMetrics(sb);

        foreach (var (name, family) in _gauges.OrderBy(k => k.Key, StringComparer.Ordinal))
            family.WriteTo(sb, name);

        foreach (var (name, family) in _counters.OrderBy(k => k.Key, StringComparer.Ordinal))
            family.WriteTo(sb, name);

        foreach (var (name, family) in _histograms.OrderBy(k => k.Key, StringComparer.Ordinal))
            family.WriteTo(sb, name);

        return sb.ToString();
    }

    private static void WriteRuntimeMetrics(StringBuilder sb)
    {
        using var process = Process.GetCurrentProcess();

        WriteScalarMetric(
            sb,
            "process_cpu_seconds_total",
            "counter",
            "Total user and system CPU time spent in seconds.",
            process.TotalProcessorTime.TotalSeconds);

        WriteScalarMetric(
            sb,
            "process_working_set_bytes",
            "gauge",
            "Process working set memory in bytes.",
            process.WorkingSet64);

        WriteScalarMetric(
            sb,
            "process_private_memory_bytes",
            "gauge",
            "Process private memory size in bytes.",
            process.PrivateMemorySize64);

        WriteScalarMetric(
            sb,
            "process_num_threads",
            "gauge",
            "Total number of OS threads in the process.",
            process.Threads.Count);

        var startTimeUnix = new DateTimeOffset(process.StartTime.ToUniversalTime()).ToUnixTimeMilliseconds() / 1000.0;
        WriteScalarMetric(
            sb,
            "process_start_time_seconds",
            "gauge",
            "Start time of the process since unix epoch in seconds.",
            startTimeUnix);

        WriteScalarMetric(
            sb,
            "dotnet_total_memory_bytes",
            "gauge",
            "Total known allocated memory in .NET GC heap in bytes.",
            GC.GetTotalMemory(forceFullCollection: false));

        WriteScalarMetric(
            sb,
            "dotnet_allocated_bytes_total",
            "counter",
            "Total bytes allocated over the lifetime of the .NET process.",
            GC.GetTotalAllocatedBytes(precise: false));

        WriteScalarMetric(
            sb,
            "dotnet_gc_pause_seconds_total",
            "counter",
            "Total time spent in .NET GC pauses in seconds.",
            GC.GetTotalPauseDuration().TotalSeconds);

        sb.Append("# HELP dotnet_gc_collection_count_total Number of .NET garbage collections per generation.\n");
        sb.Append("# TYPE dotnet_gc_collection_count_total counter\n");
        for (var gen = 0; gen <= 2; gen++)
        {
            sb.Append("dotnet_gc_collection_count_total{generation=\"")
                .Append(gen)
                .Append("\"} ")
                .Append(GC.CollectionCount(gen).ToString(CultureInfo.InvariantCulture))
                .Append('\n');
        }

        WriteScalarMetric(
            sb,
            "dotnet_threadpool_threads_total",
            "gauge",
            "Total number of threads currently in the .NET ThreadPool.",
            ThreadPool.ThreadCount);

        WriteScalarMetric(
            sb,
            "dotnet_threadpool_completed_items_total",
            "counter",
            "Total number of work items processed by the .NET ThreadPool.",
            ThreadPool.CompletedWorkItemCount);

        WriteScalarMetric(
            sb,
            "dotnet_threadpool_queue_length",
            "gauge",
            "Number of work items currently queued to the .NET ThreadPool.",
            ThreadPool.PendingWorkItemCount);

        WriteScalarMetric(
            sb,
            "dotnet_jit_compiled_methods_total",
            "counter",
            "Total number of methods compiled by the .NET JIT compiler.",
             System.Runtime.JitInfo.GetCompiledMethodCount());

        WriteScalarMetric(
            sb,
            "dotnet_jit_compilation_seconds_total",
            "counter",
            "Total time spent by the .NET JIT compiler in seconds.",
            JitInfo.GetCompilationTime().TotalSeconds);
    }

    private static void WriteScalarMetric(StringBuilder sb, string name, string type, string help, double value)
    {
        sb.Append("# HELP ").Append(name).Append(' ').Append(help).Append('\n');
        sb.Append("# TYPE ").Append(name).Append(' ').Append(type).Append('\n');
        sb.Append(name).Append(' ').Append(FormatDouble(value)).Append('\n');
    }

    private static string FormatDouble(double value) =>
        value.ToString("G17", CultureInfo.InvariantCulture);

    private sealed class CounterFamily(string help)
    {
        private readonly ConcurrentDictionary<string, CounterCell> _series = new(StringComparer.Ordinal);

        public void Add(string labels, long delta)
        {
            var cell = _series.GetOrAdd(labels, static _ => new CounterCell());
            if (delta != 0)
                Interlocked.Add(ref cell.Value, delta);
        }

        public void WriteTo(StringBuilder sb, string name)
        {
            sb.Append("# HELP ").Append(name).Append(' ').Append(help).Append('\n');
            sb.Append("# TYPE ").Append(name).Append(" counter\n");

            foreach (var (labels, cell) in _series.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                var val = Volatile.Read(ref cell.Value);
                sb.Append(name);
                if (!string.IsNullOrEmpty(labels))
                    sb.Append('{').Append(labels).Append('}');
                sb.Append(' ').Append(val.ToString(CultureInfo.InvariantCulture)).Append('\n');
            }
        }

        private sealed class CounterCell
        {
            public long Value;
        }
    }

    private sealed class GaugeFamily(string help)
    {
        private readonly ConcurrentDictionary<string, GaugeCell> _series = new(StringComparer.Ordinal);

        public void Add(string labels, long delta)
        {
            var cell = _series.GetOrAdd(labels, static _ => new GaugeCell());
            if (delta != 0)
                Interlocked.Add(ref cell.Value, delta);
        }

        public void WriteTo(StringBuilder sb, string name)
        {
            sb.Append("# HELP ").Append(name).Append(' ').Append(help).Append('\n');
            sb.Append("# TYPE ").Append(name).Append(" gauge\n");

            foreach (var (labels, cell) in _series.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                var val = Volatile.Read(ref cell.Value);
                sb.Append(name);
                if (!string.IsNullOrEmpty(labels))
                    sb.Append('{').Append(labels).Append('}');
                sb.Append(' ').Append(val.ToString(CultureInfo.InvariantCulture)).Append('\n');
            }
        }

        private sealed class GaugeCell
        {
            public long Value;
        }
    }

    private sealed class HistogramFamily(string help, double[] bounds)
    {
        private readonly ConcurrentDictionary<string, HistogramSeries> _series = new(StringComparer.Ordinal);

        public void EnsureSeries(string labels)
        {
            _series.GetOrAdd(labels, _ => new HistogramSeries(bounds.Length));
        }

        public void Observe(string labels, double value)
        {
            var series = _series.GetOrAdd(labels, _ => new HistogramSeries(bounds.Length));
            series.Observe(bounds, value);
        }

        public void WriteTo(StringBuilder sb, string name)
        {
            sb.Append("# HELP ").Append(name).Append(' ').Append(help).Append('\n');
            sb.Append("# TYPE ").Append(name).Append(" histogram\n");

            foreach (var (labels, series) in _series.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                var (bucketCounts, count, sum) = series.Snapshot();
                var prefix = string.IsNullOrEmpty(labels) ? string.Empty : labels + ",";

                for (var i = 0; i < bounds.Length; i++)
                {
                    sb.Append(name)
                        .Append("_bucket{")
                        .Append(prefix)
                        .Append("le=\"")
                        .Append(FormatDouble(bounds[i]))
                        .Append("\"} ")
                        .Append(bucketCounts[i].ToString(CultureInfo.InvariantCulture))
                        .Append('\n');
                }

                sb.Append(name)
                    .Append("_bucket{")
                    .Append(prefix)
                    .Append("le=\"+Inf\"} ")
                    .Append(count.ToString(CultureInfo.InvariantCulture))
                    .Append('\n');

                sb.Append(name).Append("_sum");
                if (!string.IsNullOrEmpty(labels))
                    sb.Append('{').Append(labels).Append('}');
                sb.Append(' ').Append(FormatDouble(sum)).Append('\n');

                sb.Append(name).Append("_count");
                if (!string.IsNullOrEmpty(labels))
                    sb.Append('{').Append(labels).Append('}');
                sb.Append(' ').Append(count.ToString(CultureInfo.InvariantCulture)).Append('\n');
            }
        }

        private sealed class HistogramSeries(int bucketCount)
        {
            private readonly object _gate = new();
            private readonly long[] _cumulativeBuckets = new long[bucketCount];
            private long _count;
            private double _sum;

            public void Observe(double[] bounds, double value)
            {
                lock (_gate)
                {
                    for (var i = 0; i < bounds.Length; i++)
                    {
                        if (value <= bounds[i])
                            _cumulativeBuckets[i]++;
                    }

                    _count++;
                    _sum += value;
                }
            }

            public (long[] Buckets, long Count, double Sum) Snapshot()
            {
                lock (_gate)
                {
                    var copy = new long[_cumulativeBuckets.Length];
                    Array.Copy(_cumulativeBuckets, copy, copy.Length);
                    return (copy, _count, _sum);
                }
            }
        }
    }
}
