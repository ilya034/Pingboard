using System.Reflection;

namespace Pingboard.Api.Infrastructure;

public sealed record AppVersionInfo(string Version, string PodName)
{
    public const string VersionHeaderName = "X-App-Version";
    public const string PodHeaderName = "X-Pod-Name";

    public static AppVersionInfo FromConfiguration(IConfiguration configuration)
    {
        var version = FirstNonEmpty(
            configuration["App:Version"],
            configuration["APP_VERSION"],
            configuration["VERSION"],
            ResolveAssemblyVersion(),
            "dev");

        var podName = FirstNonEmpty(
            configuration["POD_NAME"],
            configuration["HOSTNAME"],
            Environment.MachineName,
            "unknown");

        return new AppVersionInfo(version, podName);
    }

    private static string? ResolveAssemblyVersion()
    {
        var assembly = typeof(AppVersionInfo).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plusIndex = informational.IndexOf('+');
            return plusIndex > 0 ? informational[..plusIndex] : informational;
        }

        return assembly.GetName().Version?.ToString();
    }

    private static string FirstNonEmpty(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
                return candidate.Trim();
        }

        return "unknown";
    }
}
