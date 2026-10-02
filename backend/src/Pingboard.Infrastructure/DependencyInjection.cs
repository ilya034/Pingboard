using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pingboard.Application.Abstractions;
using Pingboard.Application.Monitors.Options;
using Pingboard.Application.Probing.Options;
using Pingboard.Domain.Entities;
using Pingboard.Infrastructure.Persistence;
using Pingboard.Infrastructure.Probing;
using Pingboard.Infrastructure.Probing.Options;
using Pingboard.Infrastructure.Repositories;
using Pingboard.Infrastructure.Security;
using Pingboard.Infrastructure.Security.Options;

namespace Pingboard.Infrastructure;

/// <summary>
///     Единственный публичный API Infrastructure-слоя: Api и Worker вызывают только его
///     и не знают ни про EF Core, ни про Npgsql (правило из §13 PLAN.md).
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
                               ?? throw new InvalidOperationException(
                                   "ConnectionStrings__Default is not set; configuration is only from environment (factor III)." );

        services.AddDbContext<UptimeDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsAssembly(typeof(UptimeDbContext).Assembly.FullName)));

        // Application-порты -> реализации Infrastructure.
        services.AddScoped<IMonitorRepository, MonitorRepository>();
        services.AddScoped<ICheckRepository, CheckRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Readiness (/readyz) спрашивает БД через порт: Api не знает про EF Core.
        services.AddScoped<IDatabaseHealthProbe, DatabaseHealthProbe>();

        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();

        // HTTP-проверки: именованный клиент, чтобы не плодить сокеты и не терять DNS.
        // Адрес проверки задаёт пользователь, поэтому соединение идёт через барьер SSRF
        // (ProbeTargetGuard): резолв и проверка адреса — в ConnectCallback, то есть
        // DNS rebinding между валидацией и коннектом не срабатывает.
        var allowPrivateNetworks = configuration.GetValue("Probe:AllowPrivateNetworks", false);

        services.AddHttpClient(HttpProbeService.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // Редирект — валидный ответ монитора, а не повод идти следом.
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.All,
                ConnectCallback = ProbeTargetGuard.CreateConnectCallback(allowPrivateNetworks)
            });

        services.AddSingleton<IProbeService, HttpProbeService>();

        // Options-паттерн: кривой конфиг должен падать на старте, а не посреди работы.
        // Секцию Jwt биндит тот слой, который её и потребляет (JwtTokenService):
        // иначе Worker, дёрнувший выдачу токена, падал бы в рантайме.
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(
                o => JwtSecretValidator.IsValid(o.Secret),
                "Jwt__Secret is shorter than 32 bytes (HS256). Generate it with: openssl rand -base64 48")
            .Validate(o => o.ExpiresMinutes > 0, "Jwt__ExpiresMinutes must be greater than zero");

        services.AddOptions<ProbeOptions>()
            .Bind(configuration.GetSection(ProbeOptions.SectionName))
            .Validate(o => o.TimeoutMs > 0, "Probe__TimeoutMs must be greater than zero")
            .Validate(o => o.HealthyStatusCodes.Length > 0, "Probe__HealthyStatusCodes cannot be empty")
            .ValidateOnStart();

        services.AddOptions<DueCheckOptions>()
            .Bind(configuration.GetSection(DueCheckOptions.SectionName))
            .Validate(o => o.BatchSize > 0, "Worker__BatchSize must be greater than zero")
            .Validate(o => o.MaxParallel > 0, "Worker__MaxParallel must be greater than zero")
            .ValidateOnStart();

        services.AddOptions<MonitorsOptions>()
            .Bind(configuration.GetSection(MonitorsOptions.SectionName))
            .Validate(o => o.UptimeWindowHours > 0, "Monitors__UptimeWindowHours must be greater than zero")
            .Validate(o => o.UptimeBarSegments > 0, "Monitors__UptimeBarSegments must be greater than zero")
            .ValidateOnStart();

        return services;
    }

    /// <summary>
    ///     Миграции на старте (режим MVP из §9 PLAN.md). В M5 заменяется на migrate-bundle —
    ///     тогда миграции становятся отдельным one-off процессом (фактор XII).
    /// </summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<UptimeDbContext>();
        await db.Database.MigrateAsync(ct);
    }

    /// <summary>
    ///     Сид демо-пользователя: учётка, которой можно войти на свежеподнятом стенде
    ///     (POST /api/auth/login). Владельцем запросов она не является — владелец берётся
    ///     из claim sub access-токена (M4).
    /// </summary>
    public static async Task<Guid> SeedDefaultUserAsync(
        this IServiceProvider services,
        Guid userId,
        string email,
        string password,
        CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<UptimeDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        // Ищем и по Id, и по email: email уникален в БД, поэтому сид «своей» учётки под
        // заданным Id упал бы на UNIQUE-индексе уже после старта — с невнятным 23505.
        var normalizedEmail = User.NormalizeEmail(email);
        var existing = await db.Users.FirstOrDefaultAsync(u => u.Id == userId || u.Email == normalizedEmail, ct);

        if (existing is not null)
        {
            if (existing.Id != userId)
                throw new InvalidOperationException(
                    $"Demo user seeding is impossible: email {normalizedEmail} is already used by another user " +
                    $"({existing.Id}). Set a different Auth__DefaultUserEmail.");

            return existing.Id;
        }

        // Домен сам генерирует Id, а сид-пользователь должен получить ровно Auth__DefaultUserId.
        var now = TimeProvider.System.GetUtcNow();
        var user = User.Register(normalizedEmail, hasher.Hash(password), now);
        db.Users.Add(user);
        db.Entry(user).Property(u => u.Id).CurrentValue = userId;

        await db.SaveChangesAsync(ct);
        return userId;
    }
}
