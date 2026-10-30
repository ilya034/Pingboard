using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Pingboard.Prober;

/// <summary>
///     Исполнитель синтетических blackbox-проверок пользовательских сценариев Pingboard:
///     готовность БД (<c>/readyz</c>), аутентификация (<c>/api/auth/*</c>) и полный цикл CRUD мониторов (<c>/api/monitors</c>).
/// </summary>
public sealed class SyntheticProberRunner(ProberOptions options, ProberMetricsExporter metrics)
{
    private const string BaselineMonitorName = "SLO Baseline Target";
    private const string EphemeralMonitorName = "Prober Ephemeral Check";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http = new()
    {
        BaseAddress = new Uri(options.ApiBaseUrl),
        Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds)
    };

    private string? _cachedAccessToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;
    private long _tickCounter;

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(options.IntervalSeconds);
        ProberLog.Info(
            $"Synthetic prober started: target={options.ApiBaseUrl}, interval={options.IntervalSeconds}s, monitorTarget={options.TargetMonitorUrl}");

        while (!stoppingToken.IsCancellationRequested)
        {
            var tick = Interlocked.Increment(ref _tickCounter);
            await ExecuteSingleCycleAsync(tick, stoppingToken);

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        ProberLog.Info("Synthetic prober stopped");
    }

    private async Task ExecuteSingleCycleAsync(long tick, CancellationToken ct)
    {
        var e2eStart = Stopwatch.GetTimestamp();
        var readyOk = await ProbeReadyzAsync(ct);

        var forceLoginProbe = string.IsNullOrEmpty(_cachedAccessToken)
                              || DateTimeOffset.UtcNow.AddMinutes(2) >= _tokenExpiresAt
                              || tick % options.LoginEveryNTicks == 0;

        var authOk = true;
        if (forceLoginProbe)
            authOk = await ProbeAuthAsync(ct);

        var crudOk = false;
        if (!string.IsNullOrEmpty(_cachedAccessToken) && authOk)
            crudOk = await ProbeMonitorsCrudAsync(ct);

        var e2eDuration = Stopwatch.GetElapsedTime(e2eStart).TotalSeconds;
        var e2eSuccess = readyOk && authOk && crudOk && e2eDuration <= options.MaxE2EDurationSeconds;

        metrics.RecordStep("e2e", e2eSuccess, e2eDuration);

        if (!e2eSuccess)
        {
            ProberLog.Warn(
                $"E2E probe failed (tick={tick}, ready={readyOk}, auth={authOk}, crud={crudOk}, duration={e2eDuration:F3}s)");
        }
    }

    private async Task<bool> ProbeReadyzAsync(CancellationToken ct)
    {
        var sw = Stopwatch.GetTimestamp();
        try
        {
            using var response = await _http.GetAsync("/readyz", ct);
            var ok = response.IsSuccessStatusCode;
            metrics.RecordStep("readyz", ok, Stopwatch.GetElapsedTime(sw).TotalSeconds);
            return ok;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            metrics.RecordStep("readyz", success: false, Stopwatch.GetElapsedTime(sw).TotalSeconds);
            ProberLog.Warn($"readyz probe exception: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> ProbeAuthAsync(CancellationToken ct)
    {
        var sw = Stopwatch.GetTimestamp();
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                email = options.UserEmail,
                password = options.UserPassword
            }, JsonOpts);

            using var loginContent = new StringContent(payload, Encoding.UTF8, "application/json");
            using var loginResp = await _http.PostAsync("/api/auth/login", loginContent, ct);

            if (loginResp.StatusCode == HttpStatusCode.Unauthorized)
            {
                // Первичная саморегистрация синтетического пользователя пробера на чистой БД.
                using var regContent = new StringContent(payload, Encoding.UTF8, "application/json");
                using var regResp = await _http.PostAsync("/api/auth/register", regContent, ct);

                if (regResp.IsSuccessStatusCode && await TryExtractTokenAsync(regResp, ct))
                {
                    metrics.RecordStep("auth", success: true, Stopwatch.GetElapsedTime(sw).TotalSeconds);
                    return true;
                }

                metrics.RecordStep("auth", success: false, Stopwatch.GetElapsedTime(sw).TotalSeconds);
                return false;
            }

            if (loginResp.IsSuccessStatusCode && await TryExtractTokenAsync(loginResp, ct))
            {
                metrics.RecordStep("auth", success: true, Stopwatch.GetElapsedTime(sw).TotalSeconds);
                return true;
            }

            metrics.RecordStep("auth", success: false, Stopwatch.GetElapsedTime(sw).TotalSeconds);
            return false;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            metrics.RecordStep("auth", success: false, Stopwatch.GetElapsedTime(sw).TotalSeconds);
            ProberLog.Warn($"auth probe exception: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> ProbeMonitorsCrudAsync(CancellationToken ct)
    {
        var sw = Stopwatch.GetTimestamp();
        try
        {
            // 1. Чтение списка мониторов (GET /api/monitors)
            using var listReq = CreateAuthorizedRequest(HttpMethod.Get, "/api/monitors");
            using var listResp = await _http.SendAsync(listReq, ct);

            if (listResp.StatusCode == HttpStatusCode.Unauthorized)
            {
                _cachedAccessToken = null;
                metrics.RecordStep("monitors_crud", success: false, Stopwatch.GetElapsedTime(sw).TotalSeconds);
                return false;
            }

            if (!listResp.IsSuccessStatusCode)
            {
                metrics.RecordStep("monitors_crud", success: false, Stopwatch.GetElapsedTime(sw).TotalSeconds);
                return false;
            }

            var listBody = await listResp.Content.ReadAsStringAsync(ct);
            var existing = JsonSerializer.Deserialize<List<MonitorSummaryJson>>(listBody, JsonOpts) ?? [];

            // Очистка возможных осиротевших временных мониторов после сбоев инжекции ошибок.
            foreach (var stale in existing.Where(m => string.Equals(m.Name, EphemeralMonitorName, StringComparison.Ordinal)))
            {
                using var cleanupReq = CreateAuthorizedRequest(HttpMethod.Delete, $"/api/monitors/{stale.Id}");
                using var _ = await _http.SendAsync(cleanupReq, ct);
            }

            // Гарантируем наличие одного постоянного монитора, чтобы Pingboard.Worker выполнял фоновые проверки.
            if (!existing.Any(m => string.Equals(m.Name, BaselineMonitorName, StringComparison.Ordinal)))
            {
                var baselineJson = JsonSerializer.Serialize(new
                {
                    name = BaselineMonitorName,
                    url = options.TargetMonitorUrl,
                    intervalSeconds = 10,
                    enabled = true
                }, JsonOpts);

                using var seedReq = CreateAuthorizedRequest(HttpMethod.Post, "/api/monitors", baselineJson);
                using var seedResp = await _http.SendAsync(seedReq, ct);
                if (!seedResp.IsSuccessStatusCode)
                {
                    metrics.RecordStep("monitors_crud", success: false, Stopwatch.GetElapsedTime(sw).TotalSeconds);
                    return false;
                }
            }

            // 2. Создание временного синтетического монитора (POST /api/monitors)
            var createJson = JsonSerializer.Serialize(new
            {
                name = EphemeralMonitorName,
                url = options.TargetMonitorUrl,
                intervalSeconds = 60,
                enabled = false
            }, JsonOpts);

            using var createReq = CreateAuthorizedRequest(HttpMethod.Post, "/api/monitors", createJson);
            using var createResp = await _http.SendAsync(createReq, ct);
            if (!createResp.IsSuccessStatusCode)
            {
                metrics.RecordStep("monitors_crud", success: false, Stopwatch.GetElapsedTime(sw).TotalSeconds);
                return false;
            }

            var createdBody = await createResp.Content.ReadAsStringAsync(ct);
            var created = JsonSerializer.Deserialize<MonitorSummaryJson>(createdBody, JsonOpts);
            if (created is null || created.Id == Guid.Empty)
            {
                metrics.RecordStep("monitors_crud", success: false, Stopwatch.GetElapsedTime(sw).TotalSeconds);
                return false;
            }

            // 3. Чтение карточки созданного монитора (GET /api/monitors/{id})
            using var getReq = CreateAuthorizedRequest(HttpMethod.Get, $"/api/monitors/{created.Id}");
            using var getResp = await _http.SendAsync(getReq, ct);
            if (!getResp.IsSuccessStatusCode)
            {
                metrics.RecordStep("monitors_crud", success: false, Stopwatch.GetElapsedTime(sw).TotalSeconds);
                return false;
            }

            // 4. Удаление временного монитора (DELETE /api/monitors/{id})
            using var delReq = CreateAuthorizedRequest(HttpMethod.Delete, $"/api/monitors/{created.Id}");
            using var delResp = await _http.SendAsync(delReq, ct);
            var ok = delResp.IsSuccessStatusCode;

            metrics.RecordStep("monitors_crud", ok, Stopwatch.GetElapsedTime(sw).TotalSeconds);
            return ok;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            metrics.RecordStep("monitors_crud", success: false, Stopwatch.GetElapsedTime(sw).TotalSeconds);
            ProberLog.Warn($"monitors_crud probe exception: {ex.Message}");
            return false;
        }
    }

    private HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string path, string? jsonBody = null)
    {
        var req = new HttpRequestMessage(method, path);
        if (!string.IsNullOrEmpty(_cachedAccessToken))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cachedAccessToken);

        if (jsonBody is not null)
            req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        return req;
    }

    private async Task<bool> TryExtractTokenAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var json = await response.Content.ReadAsStringAsync(ct);
        var dto = JsonSerializer.Deserialize<TokenJson>(json, JsonOpts);
        if (dto is null || string.IsNullOrWhiteSpace(dto.AccessToken))
            return false;

        _cachedAccessToken = dto.AccessToken;
        _tokenExpiresAt = dto.ExpiresAt == default
            ? DateTimeOffset.UtcNow.AddMinutes(30)
            : dto.ExpiresAt;
        return true;
    }

    private sealed record TokenJson(string? AccessToken, DateTimeOffset ExpiresAt);

    private sealed record MonitorSummaryJson(Guid Id, string? Name);
}
