using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Pingboard.Api.Infrastructure;

/// <summary>
/// Настройка ограничения частоты запросов (rate limiting) для конечных точек аутентификации.
/// Защищает ресурсоёмкие операции хеширования паролей (PBKDF2) от подбора и исчерпания ресурсов CPU.
/// Применяет раздельные лимиты для входа и регистрации, партиционируя запросы по IP-адресу клиента.
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
                    Title = "Too many requests.",
                    Detail = $"No more than {limit} requests per {windowSeconds} seconds. Please try again later.",
                    Instance = http.Request.Path
                };

                problem.Extensions["traceId"] = http.TraceIdentifier;

                http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await ErrorResponseFormat.WriteAsync(http, problem);
            };
        });

        return services;
    }

    /// <summary>
    /// Создаёт партицию с фиксированным окном на основе удалённого IP-адреса соединения.
    /// </summary>
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
