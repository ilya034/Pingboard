using System.Security.Cryptography;
using Pingboard.Infrastructure.Security;

namespace Pingboard.Api.Infrastructure;

/// <summary>
/// Обеспечивает наличие валидного секретного ключа подписи HS256 в конфигурации приложения до старта хоста.
/// В среде разработки генерирует временный ключ при его отсутствии или некорректности.
/// </summary>
public static class JwtSecretProvisioning
{
    /// <summary>Длина генерируемого эфемерного секрета в байтах (HS256 требует не менее 32 байт).</summary>
    private const int EphemeralSecretBytes = 48;

    /// <summary>
    /// Проверяет наличие ключа Jwt:Secret. В среде Development генерирует эфемерный ключ при отсутствии,
    /// в остальных окружениях выбрасывает исключение с требованием корректной конфигурации.
    /// </summary>
    /// <returns>Результат инициализации секрета (<see cref="JwtSecretOutcome"/>).</returns>
    public static JwtSecretOutcome EnsureDevSecret(IConfiguration configuration, bool isDevelopment)
    {
        var secret = configuration["Jwt:Secret"];
        var wasConfigured = !string.IsNullOrWhiteSpace(secret);

        if (JwtSecretValidator.IsValid(secret))
        {
            return JwtSecretOutcome.Configured;
        }

        if (!isDevelopment)
        {
            throw new InvalidOperationException(
                "Jwt__Secret is not set or is shorter than 32 bytes (HS256). Generate it with: openssl rand -base64 48 " +
                "and pass it via environment variable.");
        }

        var ephemeral = Convert.ToBase64String(RandomNumberGenerator.GetBytes(EphemeralSecretBytes));

        // Секрет попадает в конфигурацию как обычный ключ: IOptions<JwtOptions> прочитает его
        // уже сгенерированным, потому что хост ещё не построен.
        if (configuration is IConfigurationRoot root)
        {
            root["Jwt:Secret"] = ephemeral;
        }
        else if (configuration is IConfigurationBuilder builder)
        {
            builder.AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Secret"] = ephemeral });
        }
        else
        {
            throw new InvalidOperationException(
                "The current configuration cannot set the generated Jwt__Secret: IConfigurationRoot is required.");
        }

        // Разница важна для диагностики: «секрет не задан» и «секрет задан, но негоден» —
        // разные ошибки конфигурации, и вторая всплыла бы только в проде, где она роняет старт.
        return wasConfigured ? JwtSecretOutcome.ReplacedInvalid : JwtSecretOutcome.EphemeralGenerated;
    }
}
