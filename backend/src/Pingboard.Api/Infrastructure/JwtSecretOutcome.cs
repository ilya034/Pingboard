namespace Pingboard.Api.Infrastructure;

/// <summary>Что произошло с ключом подписи на старте Api (см. <see cref="JwtSecretProvisioning" />).</summary>
public enum JwtSecretOutcome
{
    /// <summary>Секрет задан и валиден — работаем на нём.</summary>
    Configured,

    /// <summary>Секрет не задан, в Development сгенерирован эфемерный (токены не переживут рестарт).</summary>
    EphemeralGenerated,

    /// <summary>Секрет задан, но не проходит проверку: в Development он подменён эфемерным.</summary>
    ReplacedInvalid
}
