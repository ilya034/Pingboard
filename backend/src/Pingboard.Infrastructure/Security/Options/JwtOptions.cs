using System.Text;

namespace Pingboard.Infrastructure.Security.Options;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>HS256 требует ключ не короче 32 байт.</summary>
    public string Secret { get; set; } = string.Empty;

    public string Issuer { get; set; } = "pingboard";

    public string Audience { get; set; } = "pingboard";

    public int ExpiresMinutes { get; set; } = 120;

    public static bool IsSecretValid(string? secret)
    {
        return Encoding.UTF8.GetByteCount(secret ?? string.Empty) >= 32;
    }
}
