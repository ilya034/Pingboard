using Pingboard.Api.Endpoints;
using Pingboard.Api.Infrastructure;
using Pingboard.Application;
using Pingboard.Application.Abstractions;
using Pingboard.Infrastructure;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Режим применения миграций базы данных при запуске с аргументом --migrate.
if (args.Contains("--migrate", StringComparer.Ordinal))
{
    // DI-контейнер для выполнения миграций: AddInfrastructure регистрирует контекст БД,
    // которому требуется TimeProvider из AddApplication.
    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);

    await using var migrateApp = builder.Build();
    await migrateApp.Services.MigrateDatabaseAsync();

    return;
}

// Секрет подписи нужен до старта хоста: из него читают ключ JwtTokenService и AddJwtBearer.
// В Development секрет может быть сгенерирован автоматически.
var secretOutcome = JwtSecretProvisioning.EnsureDevSecret(builder.Configuration, builder.Environment.IsDevelopment());

// Настройка структурированного логирования в формате JSON.
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "yyyy-MM-ddTHH\\:mm\\:ss.fffZ";
    options.UseUtcTimestamp = true;
});

// Регистрация слоёв приложения и инфраструктуры.
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var chaosEnabled = builder.Environment.IsDevelopment()
                   || builder.Configuration.GetValue("Chaos:Enabled", false);
if (chaosEnabled)
    builder.Services.AddSingleton<ChaosSimulationState>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, JwtUserIdProvider>();

builder.Services.AddOpenApi(options => options.AddBearerSecurity());
builder.Services.AddJwtAuthentication(builder.Configuration);

// Лимит на /api/auth/*: каждый запрос там считает PBKDF2, то есть это CPU-ручка.
// Политики раздельные (логин / регистрация) — см. AuthRateLimiting.
builder.Services.AddAuthRateLimiting(builder.Configuration);

// Заголовки прокси: по умолчанию выключены, включаются только явной настройкой.
// Порядок в конвейере важен: разбор X-Forwarded-For обязан идти до UseRateLimiter,
// иначе ключ раздела лимита так и останется адресом прокси.
var forwardedHeadersEnabled = builder.Services.AddProxyForwardedHeaders(builder.Configuration);

var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
                  ?? ["http://localhost:5173"];

builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(corsOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()));

// Таймаут корректного завершения работы приложения (graceful shutdown).
builder.Services.Configure<HostOptions>(options => { options.ShutdownTimeout = TimeSpan.FromSeconds(10); });

var app = builder.Build();

if (secretOutcome == JwtSecretOutcome.ReplacedInvalid)
    app.Logger.LogWarning(
        "Jwt__Secret is set but fails validation (HS256 requires at least 32 bytes): " +
        "in Development it is replaced with an ephemeral key for this run. " +
        "In any other environment, this same secret will fail startup; fix the value.");
else if (secretOutcome == JwtSecretOutcome.EphemeralGenerated)
    app.Logger.LogWarning(
        "Jwt__Secret is not set: an ephemeral signing key was generated for this run only. " +
        "Issued tokens will become invalid after restart. For a staging environment, set Jwt__Secret.");

// Автоматическое применение миграций БД при старте (по умолчанию включено в Development).
var migrateOnStart = builder.Configuration.GetValue("MigrateOnStart", app.Environment.IsDevelopment());

if (migrateOnStart)
    try
    {
        await app.Services.MigrateDatabaseAsync();
    }
    catch (Exception ex)
    {
        // Приложение запускается даже при временной недоступности БД: /healthz возвращает 200, а /readyz — 503.
        app.Logger.LogWarning(ex, "Migration not applied: DB unavailable");
    }

// Инициализация демонстрационной учётной записи (только для среды Development).
var seedOnStart = builder.Configuration.GetValue("SeedOnStart", app.Environment.IsDevelopment());

// Пароль демо-учётки лежит в открытом исходнике (DemoUser), поэтому вне Development
// сид запрещён: это был бы вход в систему с публично известными кредами. Аккаунт
// на стенде создаётся обычной регистрацией.
if (seedOnStart && !app.Environment.IsDevelopment())
    throw new InvalidOperationException(
        "SeedOnStart is enabled outside Development. The demo account password is known from source code: " +
        "disable SeedOnStart and create a user via POST /api/auth/register.");

if (seedOnStart)
    try
    {
        await app.Services.SeedDefaultUserAsync(
            DemoUser.Id,
            builder.Configuration["Auth:DefaultUserEmail"] ?? DemoUser.Email,
            builder.Configuration["Auth:DefaultUserPassword"] ?? DemoUser.Password);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Seed demo user not completed");
    }

// Инициализация синглтона бизнес-метрик при старте приложения.
_ = app.Services.GetRequiredService<IPingboardMetrics>();
_ = app.Services.GetService<ChaosSimulationState>();

app.UseHttpMetrics();
app.UseApiErrorHandling();

if (chaosEnabled)
    app.UseChaosSimulation();

// Первым: без этого rate limiter и логи видят адрес прокси, а не клиента.
if (forwardedHeadersEnabled)
    app.UseForwardedHeaders();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
// Ставится после авторизации и до маршрутов: 429 отдаётся в формате ProblemDetails (OnRejected).
app.UseRateLimiter();

// OpenAPI-документ и Scalar — только в Development: в проде это анонимный доступ
// ко всей поверхности API (все маршруты, схемы, коды ответов).
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi(); // /openapi/v1.json
    app.MapScalarApiReference(); // /scalar/v1 - UI для проверки CRUD до готового фронта
}

if (chaosEnabled)
    app.MapChaosEndpoints();

app.MapHealthEndpoints();
app.MapAuthEndpoints();
app.MapMonitorEndpoints();

app.Run();
