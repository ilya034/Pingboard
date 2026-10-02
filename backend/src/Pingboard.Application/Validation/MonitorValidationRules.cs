using Pingboard.Domain.Common;

namespace Pingboard.Application.Validation;

internal static class MonitorValidationRules
{
    public static void CheckName(ValidationResult result, string? name, bool required)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            if (required) result.Add(nameof(name), "Имя обязательно.");

            return;
        }

        if (name.Trim().Length > MonitorRules.NameMaxLength)
            result.Add(nameof(name), $"Имя не длиннее {MonitorRules.NameMaxLength} символов.");
    }

    public static void CheckUrl(ValidationResult result, string? url, bool required)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            if (required) result.Add(nameof(url), "URL обязателен.");

            return;
        }

        if (!MonitorRules.IsSupportedUrl(url))
            result.Add(nameof(url), "Нужен абсолютный URL со схемой http или https.");
    }

    public static void CheckInterval(ValidationResult result, int? intervalSeconds)
    {
        if (intervalSeconds is null) return;

        if (!MonitorRules.IsValidInterval(intervalSeconds.Value))
            result.Add(
                nameof(intervalSeconds),
                $"Интервал должен быть в диапазоне {MonitorRules.IntervalSecondsMin}..{MonitorRules.IntervalSecondsMax} секунд.");
    }
}
