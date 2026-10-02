using Pingboard.Api.Endpoints;
using Pingboard.Api.Infrastructure;
using Pingboard.Application;
using Pingboard.Application.Abstractions;
using Pingboard.Infrastructure;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Админ-режим «только схема» (фактор XII): тот же образ, отдельная команда и отдельный процесс.
if (args.Contains("--migrate", StringComparer.Ordinal))
{
    // Тот же DI-корень, что и у веб-процесса: AddInfrastructure регистрирует сервисы
    // (JwtTokenService, HttpProbeService), которым нужен TimeProvider из AddApplication.
    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);

    await using var migrateApp = builder.Build();
    await migrateApp.Services.MigrateDatabaseAsync();

    return;
}

// Секрет подписи нужен до старта хоста: из него одинаково читают ключ и выдача
// (JwtTokenService), и проверка (AddJwtBearer). В Development секрет можно не задавать.
var secretOutcome = JwtSecretProvisioning.EnsureDevSecret(builder.Configuration, builder.Environment.IsDevelopment());

// Конфигурация
// Только env (фактор III): appsettings.json содержит dev-умолчания без секретов,
// всё, что отличается в проде, приходит переменными вида Worker__MaxParallel.
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "yyyy-MM-ddTHH\\:mm\\:ss.fffZ";
    options.UseUtcTimestamp = true;
});

// DI-корень
// Api знает про Infrastructure ровно настолько, чтобы вызвать AddInfrastructure():
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

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

// Graceful shutdown(фактор IX).
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

// Startup-задачи: миграции и сид демо-пользователя
// MVP (M0): MigrateOnStart=true — Api сам приводит схему в порядок.
// M5: тот же шаг выполняется отдельным процессом pingboard-migrate (фактор XII).
var migrateOnStart = builder.Configuration.GetValue("MigrateOnStart", app.Environment.IsDevelopment());

if (migrateOnStart)
    try
    {
        await app.Services.MigrateDatabaseAsync();
    }
    catch (Exception ex)
    {
        // Приложение поднимается даже с недоступной БД: /healthz зелёный, /readyz — 503.
        app.Logger.LogWarning(ex, "Migration not applied: DB unavailable");
    }

// Сид демо-пользователя: после включения JWT (M4) это учётка, которой можно войти
// в свежеподнятый стенд (POST /api/auth/login), а не «владелец по умолчанию».
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

app.UseApiErrorHandling();

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

app.MapHealthEndpoints();
app.MapAuthEndpoints();
app.MapMonitorEndpoints();

app.Run();
