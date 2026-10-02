using Pingboard.Domain.Common;

namespace Pingboard.Application.Validation;

internal static class MonitorValidationRules
{
    public static void CheckName(ValidationResult result, string? name, bool required)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            if (required) result.Add(nameof(name), "Name is required.");

            return;
        }

        if (name.Trim().Length > MonitorRules.NameMaxLength)
            result.Add(nameof(name), $"Name must not exceed {MonitorRules.NameMaxLength} characters.");
    }

    public static void CheckUrl(ValidationResult result, string? url, bool required)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            if (required) result.Add(nameof(url), "URL is required.");

            return;
        }

        if (!MonitorRules.IsSupportedUrl(url))
            result.Add(nameof(url), "An absolute URL with http or https scheme is required.");
    }

    public static void CheckInterval(ValidationResult result, int? intervalSeconds)
    {
        if (intervalSeconds is null) return;

        if (!MonitorRules.IsValidInterval(intervalSeconds.Value))
            result.Add(
                nameof(intervalSeconds),
                $"Interval must be in the range {MonitorRules.IntervalSecondsMin}..{MonitorRules.IntervalSecondsMax} seconds.");
    }
}
