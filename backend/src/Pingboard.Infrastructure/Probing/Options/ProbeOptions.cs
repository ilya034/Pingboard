namespace Pingboard.Infrastructure.Probing.Options;

public sealed class ProbeOptions
{
    public const string SectionName = "Probe";

    /// <summary>Таймаут одной проверки, мс.</summary>
    public int TimeoutMs { get; set; } = 5_000;

    /// <summary>Какие коды считаем «живым» сервисом.</summary>
    public int[] HealthyStatusCodes { get; set; } =
        [200, 201, 202, 203, 204, 205, 206, 207, 208, 226, 300, 301, 302, 303, 304, 307, 308];

    /// <summary>Клиент ходит HEAD, а на 405/501 повторяет GET — так дешевле и совместимо с серверами.</summary>
    public bool UseHeadWithGetFallback { get; set; } = true;
}
