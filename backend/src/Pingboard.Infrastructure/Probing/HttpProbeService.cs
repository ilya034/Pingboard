using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pingboard.Application.Abstractions;
using Pingboard.Domain.Common;
using Pingboard.Infrastructure.Probing.Options;

namespace Pingboard.Infrastructure.Probing;

/// <summary>
///     Реальный HTTP-проверщик. Работает через IHttpClientFactory: сокеты переиспользуются,
///     таймауты не «залипают» — правильная работа с исходящими соединениями.
///     Адрес задаёт пользователь, поэтому соединение идёт через барьер <see cref="ProbeTargetGuard" />:
///     только публичные адреса, внутренние отклоняются с внятной ошибкой (снимается настройкой
///     <c>Probe__AllowPrivateNetworks=true</c> на доверенном стенде, README §6.5).
/// </summary>
public sealed class HttpProbeService(
    IHttpClientFactory httpClientFactory,
    IOptions<ProbeOptions> options,
    TimeProvider timeProvider,
    ILogger<HttpProbeService> logger) : IProbeService
{
    public const string HttpClientName = "probe";

    /// <summary>
    ///     Сколько байт тела GET-ответа готовы дожать ради возврата соединения в пул.
    ///     Больше — не выкачиваем: проверке нужен статус, а не содержимое страницы.
    /// </summary>
    private const int MaxDrainBytes = 64 * 1024;

    private readonly HashSet<int> _healthy = [.. options.Value.HealthyStatusCodes];

    public async Task<ProbeOutcome> CheckAsync(string url, CancellationToken ct)
    {
        var cfg = options.Value;

        var outcome = await AttemptAsync(HttpMethod.Head, url, ct);

        // Некоторые серверы не умеют HEAD: повторяем тем же телом, но через GET.
        // Бюджет времени у фолбэка свой: если HEAD съел все 5 секунд, GET всё равно должен
        // успеть — иначе живой сайт, не умеющий HEAD, навсегда записывался бы в «Таймаут».
        if (cfg.UseHeadWithGetFallback &&
            outcome.StatusCode is (int)HttpStatusCode.MethodNotAllowed or (int)HttpStatusCode.NotImplemented)
            outcome = await AttemptAsync(HttpMethod.Get, url, ct);

        return outcome;
    }

    /// <summary>Одна попытка со своим таймаутом: CancelAfter внутри, а не на весь CheckAsync.</summary>
    private async Task<ProbeOutcome> AttemptAsync(HttpMethod method, string url, CancellationToken ct)
    {
        var cfg = options.Value;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(Math.Max(1, cfg.TimeoutMs));

        try
        {
            return await SendAsync(method, url, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Отменён именно таймаут проверки, а не остановка приложения.
            return ProbeOutcome.Failure($"Timeout {cfg.TimeoutMs} ms");
        }
        catch (HttpRequestException ex)
        {
            logger.LogDebug(ex, "Check for {Url} failed", url);
            return ProbeOutcome.Failure($"{ex.HttpRequestError}: {ex.Message}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Unexpected check error for {Url}", url);
            return ProbeOutcome.Failure(ex.Message);
        }
    }

    private async Task<ProbeOutcome> SendAsync(HttpMethod method, string url, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(method, url);
        request.Headers.UserAgent.ParseAdd("Pingboard/0.1 (+uptime-monitor)");

        var startedAt = timeProvider.GetTimestamp();

        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            var latency = ElapsedMs(startedAt);
            var status = (int)response.StatusCode;

            // Замер сделан — теперь можно дожать тело GET, чтобы соединение вернулось в пул.
            if (method == HttpMethod.Get)
                await DrainAsync(response, ct);

            return _healthy.Contains(status)
                ? ProbeOutcome.Success(status, latency)
                : ProbeOutcome.BadStatus(status, latency);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is not null)
        {
            // 4xx/5xx без тела не бросается, но некоторые клиенты кидают с кодом — учтём и это.
            var latency = ElapsedMs(startedAt);
            return ProbeOutcome.BadStatus((int)ex.StatusCode.Value, latency);
        }
    }

    /// <summary>
    ///     Дочитывает тело ответа: пока оно не прочитано до конца, соединение не возвращается
    ///     в пул, и каждый GET-фолбэк открывал бы новый TCP/TLS-хендшейк.
    ///     Ошибки чтения и таймаут здесь глотаются осознанно: статус уже получен, и «не докачали
    ///     тело» — не повод менять результат проверки.
    /// </summary>
    private static async Task DrainAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength is > MaxDrainBytes) return;

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var buffer = new byte[8 * 1024];
            var total = 0;

            while (total < MaxDrainBytes)
            {
                var read = await stream.ReadAsync(buffer, ct);
                if (read == 0) break;

                total += read;
            }
        }
        catch (Exception)
        {
            // см. XML-doc: результат проверки уже определён статусом
        }
    }

    private int ElapsedMs(long startedAt)
    {
        return (int)Math.Max(0, timeProvider.GetElapsedTime(startedAt).TotalMilliseconds);
    }
}
