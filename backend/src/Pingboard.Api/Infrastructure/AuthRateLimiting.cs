using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Pingboard.Api.Infrastructure;

/// <summary>
///     Rate limit на <c>/api/auth/*</c>: подбор пароля и «пакетная» регистрация — дешёвый
///     способ занять CPU, потому что каждый запрос считает PBKDF2 с 210k итераций.
///     Счётчик в памяти процесса: для одного инстанса этого достаточно; при масштабировании
///     (фактор VIII) нужен распределённый счётчик — это M5.
///     Политики раздельные: логин и регистрация отличаются ценой (регистрация ещё и пишет
///     в БД), а общий лимит в 60/мин позволял с одного адреса ~86 тысяч регистраций в сутки.
///     ВАЖНО про прокси: ключ раздела — адрес соединения. За nginx все клиенты приходят
///     с адресом прокси, то есть лимит становится общим на весь стенд; чтобы разделять
///     по реальному клиенту, включается ForwardedHeaders
///     (см. <see cref="ForwardedHeadersSetup" />) — и только за доверенным прокси.
/// </summary>
internal static class AuthRateLimiting
{
    public const string LoginPolicyName = "auth-login";

    public const string RegisterPolicyName = "auth-register";

    private const int DefaultLoginPermitLimit = 20;
    private const int DefaultRegisterPermitLimit = 10;
    private const int DefaultWindowSeconds = 60;

    public static IServiceCollection AddAuthRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        // RateLimit:AuthPermitLimit — старый общий ключ; если он задан, он остаётся
        // значением по умолчанию для обеих политик, чтобы существующий .env не «потерял» настройку.
        var legacyLimit = configuration.GetValue<int?>("RateLimit:AuthPermitLimit");

        var loginLimit = Math.Max(1, configuration.GetValue(
            "RateLimit:LoginPermitLimit", legacyLimit ?? DefaultLoginPermitLimit));

        var registerLimit = Math.Max(1, configuration.GetValue(
            "RateLimit:RegisterPermitLimit", legacyLimit ?? DefaultRegisterPermitLimit));

        var windowSeconds = Math.Max(1, configuration.GetValue("RateLimit:AuthWindowSeconds", DefaultWindowSeconds));

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(LoginPolicyName, http => Partition(http, loginLimit, windowSeconds));
            options.AddPolicy(RegisterPolicyName, http => Partition(http, registerLimit, windowSeconds));

            // Отказ отдаём тем же форматом, что и остальные ошибки: ProblemDetails + traceId.
            options.OnRejected = async (context, ct) =>
            {
                var http = context.HttpContext;

                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    http.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

                // Лимит в тексте ошибки — тот, что реально применился: политика известна
                // по метаданным endpoint'а, глобального лимитера у нас нет.
                var policy = http.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
                var limit = policy == RegisterPolicyName ? registerLimit : loginLimit;

                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Слишком много запросов.",
                    Detail = $"Не больше {limit} запросов за {windowSeconds} с. Повторите позже.",
                    Instance = http.Request.Path
                };

                problem.Extensions["traceId"] = http.TraceIdentifier;

                http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await ErrorResponseFormat.WriteAsync(http, problem);
            };
        });

        return services;
    }

    /// <summary>Раздел лимита — адрес соединения (за прокси это адрес прокси, см. класс).</summary>
    private static RateLimitPartition<string> Partition(HttpContext http, int permitLimit, int windowSeconds) =>
        RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromSeconds(windowSeconds),
                // Очередь не держим: клиент должен получить 429 сразу, а не «повиснуть» на минуту.
                QueueLimit = 0,
                AutoReplenishment = true
            });
}
