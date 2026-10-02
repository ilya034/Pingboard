using System.Text;

namespace Pingboard.Infrastructure.Security;

/// <summary>
///     Одна проверка длины ключа для всех мест: и для выдачи токена, и для его валидации.
///     HS256 (HMAC-SHA256) требует ключ не короче 32 байт.
///     Живёт рядом с <see cref="Options.JwtOptions" />: секция Jwt биндится и валидируется
///     в Infrastructure, а Api добавляет только fail-fast (ValidateOnStart).
/// </summary>
public static class JwtSecretValidator
{
    public const int MinimumBytes = 32;

    public static bool IsValid(string? secret)
    {
        return Encoding.UTF8.GetByteCount(secret ?? string.Empty) >= MinimumBytes;
    }
}
