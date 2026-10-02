namespace Pingboard.Domain.Common;

/// <summary>Данные одной проверки, как их вернул пробер. Домен ничего не знает про HttpClient.</summary>
/// <param name="Ok">Успешна ли проверка (по правилам пробера: 2xx/3xx).</param>
/// <param name="StatusCode">HTTP-статус, если ответ вообще получен.</param>
/// <param name="LatencyMs">Замеренная длительность запроса.</param>
/// <param name="Error">Текст ошибки для неуспешной проверки (таймаут, DNS, TLS, ...).</param>
public readonly record struct ProbeOutcome(bool Ok, int? StatusCode, int? LatencyMs, string? Error)
{
    public static ProbeOutcome Success(int statusCode, int latencyMs)
    {
        return new ProbeOutcome(true, statusCode, latencyMs, null);
    }

    /// <summary>Ответ получен, но статус — «плохой» (5xx и т.п.).</summary>
    public static ProbeOutcome BadStatus(int statusCode, int latencyMs)
    {
        return new ProbeOutcome(false, statusCode, latencyMs, $"HTTP {statusCode}");
    }

    /// <summary>Ответа нет: таймаут, DNS, отказ соединения, TLS.</summary>
    public static ProbeOutcome Failure(string error, int? latencyMs = null)
    {
        return new ProbeOutcome(false, null, latencyMs, error);
    }
}