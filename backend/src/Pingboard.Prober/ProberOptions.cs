using System.Globalization;

namespace Pingboard.Prober;

public sealed class ProberOptions
{
    public string ApiBaseUrl { get; init; } = "http://api:8080";

    public int IntervalSeconds { get; init; } = 5;

    public int TimeoutSeconds { get; init; } = 5;

    public int LoginEveryNTicks { get; init; } = 3;

    public double MaxE2EDurationSeconds { get; init; } = 2.0;

    public int MetricsPort { get; init; } = 9101;

    public string UserEmail { get; init; } = "prober@pingboard.local";

    public string UserPassword { get; init; } = "prober-password-123";

    public string TargetMonitorUrl { get; init; } = "http://web.pingboard.svc.cluster.local/";

    public static ProberOptions FromEnvironment()
    {
        return new ProberOptions
        {
            ApiBaseUrl = GetEnv("Prober__ApiBaseUrl", "http://api:8080").TrimEnd('/'),
            IntervalSeconds = Math.Max(1, GetInt("Prober__IntervalSeconds", 5)),
            TimeoutSeconds = Math.Max(1, GetInt("Prober__TimeoutSeconds", 5)),
            LoginEveryNTicks = Math.Max(1, GetInt("Prober__LoginEveryNTicks", 3)),
            MaxE2EDurationSeconds = Math.Max(0.1, GetDouble("Prober__MaxE2EDurationSeconds", 2.0)),
            MetricsPort = GetInt("Prober__MetricsPort", 9101),
            UserEmail = GetEnv("Prober__UserEmail", "prober@pingboard.local"),
            UserPassword = GetEnv("Prober__UserPassword", "prober-password-123"),
            TargetMonitorUrl = GetEnv("Prober__TargetMonitorUrl", "http://web.pingboard.svc.cluster.local/")
        };
    }

    private static string GetEnv(string key, string fallback)
    {
        var raw = Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(raw) ? fallback : raw.Trim();
    }

    private static int GetInt(string key, int fallback)
    {
        var raw = Environment.GetEnvironmentVariable(key);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var val) ? val : fallback;
    }

    private static double GetDouble(string key, double fallback)
    {
        var raw = Environment.GetEnvironmentVariable(key);
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var val) ? val : fallback;
    }
}
