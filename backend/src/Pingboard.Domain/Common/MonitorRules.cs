namespace Pingboard.Domain.Common;

/// <summary>
///     Константы и правила валидации параметров монитора.
/// </summary>
public static class MonitorRules
{
    public const int NameMinLength = 1;
    public const int NameMaxLength = 100;

    public const int IntervalSecondsMin = 10;
    public const int IntervalSecondsMax = 86_400;
    public const int IntervalSecondsDefault = 60;

    public const int UrlMaxLength = 2048;

    /// <summary>URL обязан быть абсолютным и с схемой http/https.</summary>
    public static bool IsSupportedUrl(string? url)
    {
        return !string.IsNullOrWhiteSpace(url)
               && url.Length <= UrlMaxLength
               && Uri.TryCreate(url, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    public static bool IsValidInterval(int seconds)
    {
        return seconds is >= IntervalSecondsMin and <= IntervalSecondsMax;
    }

    public static bool IsValidName(string? name)
    {
        return !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= NameMaxLength;
    }
}
