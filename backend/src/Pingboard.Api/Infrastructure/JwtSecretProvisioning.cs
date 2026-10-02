using System.Security.Cryptography;
using Pingboard.Infrastructure.Security;

namespace Pingboard.Api.Infrastructure;

/// <summary>
///     Ключ подписи HS256 нужно иметь до старта хоста: он попадает в конфигурацию
///     (Jwt:Secret), откуда его одинаково читают и <c>JwtTokenService</c> (выдача),
///     и <c>AddJwtBearer</c> (проверка). Иначе подпись и валидация разошлись бы.
/// </summary>
public static class JwtSecretProvisioning
{
    /// <summary>Сколько байт энтропии генерируем в dev-режиме (HS256 требует ≥ 32).</summary>
    private const int EphemeralSecretBytes = 48;

    /// <summary>
    ///     В Development секрет можно не задавать — сгенерируем эфемерный, чтобы
    ///     register/login работали «из коробки». В остальных окружениях отсутствие
    ///     секрета — ошибка конфигурации, и её лучше поймать здесь, чем получить
    ///     API, который принимает токены, подписанные известным всем ключом.
    ///     Возвращает, что именно случилось: вызывающий сам решает, как об этом сказать
    ///     в логе (никакого статического состояния у класса нет).
    /// </summary>
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
                "and pass it via environment variable (factor III).");
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
