using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Pingboard.Prober;

public sealed class ProberMetricsExporter
{
    public const string ContentType = "text/plain; version=0.0.4; charset=utf-8";

    private static readonly double[] DefaultBuckets =
        [0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1.0, 2.5, 5.0, 10.0];

    private static readonly string[] KnownSteps = ["readyz", "auth", "monitors_crud", "e2e"];

    private readonly ConcurrentDictionary<string, CounterCell> _checks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, HistogramSeries> _durations = new(StringComparer.Ordinal);
    private long _proberUp = 1;
    private double _lastSuccessTimestampSeconds;

    public ProberMetricsExporter()
    {
        foreach (var step in KnownSteps)
        {
            _checks.TryAdd($"step=\"{step}\",status=\"success\"", new CounterCell());
            _checks.TryAdd($"step=\"{step}\",status=\"failure\"", new CounterCell());
            _durations.TryAdd($"step=\"{step}\"", new HistogramSeries(DefaultBuckets.Length));
        }
    }

    public void RecordStep(string step, bool success, double durationSeconds)
    {
        var status = success ? "success" : "failure";
        var counterKey = $"step=\"{step}\",status=\"{status}\"";
        var cell = _checks.GetOrAdd(counterKey, static _ => new CounterCell());
        Interlocked.Increment(ref cell.Value);

        var histKey = $"step=\"{step}\"";
        var hist = _durations.GetOrAdd(histKey, static _ => new HistogramSeries(DefaultBuckets.Length));
        hist.Observe(DefaultBuckets, Math.Max(0.0, durationSeconds));

        if (string.Equals(step, "e2e", StringComparison.Ordinal))
        {
            Interlocked.Exchange(ref _proberUp, success ? 1L : 0L);
            if (success)
            {
                var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
                Volatile.Write(ref _lastSuccessTimestampSeconds, nowUnix);
            }
        }
    }

    public string Scrape()
    {
        var sb = new StringBuilder(4096);

        using (var process = Process.GetCurrentProcess())
        {
            WriteScalar(sb, "process_cpu_seconds_total", "counter",
                "Total user and system CPU time spent in seconds.",
                process.TotalProcessorTime.TotalSeconds);
            WriteScalar(sb, "process_working_set_bytes", "gauge",
                "Process working set memory in bytes.",
                process.WorkingSet64);
        }

        WriteScalar(sb, "pingboard_prober_up", "gauge",
            "Result of the most recent synthetic end-to-end probe (1 = success, 0 = failure).",
            Interlocked.Read(ref _proberUp));

        WriteScalar(sb, "pingboard_prober_last_success_timestamp_seconds", "gauge",
            "Unix timestamp in seconds of the most recent successful synthetic end-to-end probe.",
            Volatile.Read(ref _lastSuccessTimestampSeconds));

        sb.Append("# HELP pingboard_prober_checks_total Total number of synthetic prober step executions by step and status.\n");
        sb.Append("# TYPE pingboard_prober_checks_total counter\n");
        foreach (var (labels, cell) in _checks.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            sb.Append("pingboard_prober_checks_total{")
                .Append(labels)
                .Append("} ")
                .Append(Volatile.Read(ref cell.Value).ToString(CultureInfo.InvariantCulture))
                .Append('\n');
        }

        sb.Append("# HELP pingboard_prober_duration_seconds Duration of synthetic prober steps in seconds.\n");
        sb.Append("# TYPE pingboard_prober_duration_seconds histogram\n");
        foreach (var (labels, series) in _durations.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            var (buckets, count, sum) = series.Snapshot();
            for (var i = 0; i < DefaultBuckets.Length; i++)
            {
                sb.Append("pingboard_prober_duration_seconds_bucket{")
                    .Append(labels)
                    .Append(",le=\"")
                    .Append(FormatDouble(DefaultBuckets[i]))
                    .Append("\"} ")
                    .Append(buckets[i].ToString(CultureInfo.InvariantCulture))
                    .Append('\n');
            }

            sb.Append("pingboard_prober_duration_seconds_bucket{")
                .Append(labels)
                .Append(",le=\"+Inf\"} ")
                .Append(count.ToString(CultureInfo.InvariantCulture))
                .Append('\n');

            sb.Append("pingboard_prober_duration_seconds_sum{")
                .Append(labels)
                .Append("} ")
                .Append(FormatDouble(sum))
                .Append('\n');

            sb.Append("pingboard_prober_duration_seconds_count{")
                .Append(labels)
                .Append("} ")
                .Append(count.ToString(CultureInfo.InvariantCulture))
                .Append('\n');
        }

        return sb.ToString();
    }

    private static void WriteScalar(StringBuilder sb, string name, string type, string help, double value)
    {
        sb.Append("# HELP ").Append(name).Append(' ').Append(help).Append('\n');
        sb.Append("# TYPE ").Append(name).Append(' ').Append(type).Append('\n');
        sb.Append(name).Append(' ').Append(FormatDouble(value)).Append('\n');
    }

    private static string FormatDouble(double value) =>
        value.ToString("G17", CultureInfo.InvariantCulture);

    private sealed class CounterCell
    {
        public long Value;
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
