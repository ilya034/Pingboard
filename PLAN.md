# Pingboard — план проекта: uptime-монитор для SRE-курса

> Рабочее имя **Pingboard** (мини-UptimeRobot). Переименовать можно в любой момент — структура не изменится.
> Стек: **.NET 10 (LTS)** + Clean Architecture на беке, **React (Vite + TS)** на фронте, **PostgreSQL**.
> Оценка: полный MVP ≈ 5–7 вечеров, минимальная сдача (M0–M3) ≈ 3–4 вечера.

## 1. Что это и почему подходит

Пользователь добавляет URL для наблюдения → фоновый воркер по расписанию делает HTTP-проверки → результаты пишутся в PostgreSQL → дашборд показывает статус, uptime % и историю задержек.

Почему это идеальный сюжет для курса SRE:

- полноценный клиент-сервер: SPA + REST API + БД;
- CRUD (мониторы), REST, PostgreSQL, JWT-авторизация (отдельная отключаемая веха);
- **два типа процессов (web + worker)** — наглядная демонстрация факторов VI (stateless) и VIII (concurrency);
- приложение само про наблюдаемость и uptime — раздел отчёта «пишется сам»;
- не ресурсоёмко: всё приложение + БД укладываются в ~150–200 МБ RAM, старт контейнеров — секунды.

## 2. Стек

| Слой | Выбор | Комментарий |
|---|---|---|
| Runtime | .NET 10 (LTS до 14.11.2028), C# 14 | `net10.0` |
| API | ASP.NET Core, Minimal APIs | тонкие endpoints, минимум церемоний |
| ORM/БД | EF Core 10 + Npgsql, миграции EF | миграции = фактор XII |
| Валидация | FluentValidation | внутри Application-слоя |
| Auth | JWT Bearer + BCrypt.Net-Next | без тяжёлого ASP.NET Identity |
| Метрики | prometheus-net.AspNetCore | `/metrics` из коробки |
| Логи | встроенный `AddJsonConsole()` | JSON в stdout; Serilog — опция на потом |
| Время | `System.TimeProvider` | встроенная тестируемая абстракция вместо своего IClock |
| Тесты | xUnit + фейки портов | Testcontainers — опция |
| Фронт | Vite + React + TS, react-router-dom, @tanstack/react-query, axios, recharts | поллинг через react-query |
| Инфра | Docker multi-stage, docker compose, nginx:alpine | факторы V и X |

Docker-образы: `mcr.microsoft.com/dotnet/sdk:10.0` (сборка), `mcr.microsoft.com/dotnet/aspnet:10.0` (рантайм), `postgres:18-alpine` (годится любая 16+), `node:22-alpine` (сборка фронта).

## 3. Clean Architecture

### Правило зависимостей

```
Domain  ←  Application  ←  Infrastructure
(ядро)     (use cases +      (адаптеры: EF Core,
            порты)           HttpClient, JWT, BCrypt)
                ↑                    ↑
          Api / Worker — точки композиции: env-конфиг → DI → запуск
```

- **Domain** — сущности и бизнес-правила. Ноль NuGet-зависимостей.
- **Application** — сценарии (use cases), DTO, интерфейсы портов («что сценариям нужно от мира»). Зависит только от Domain.
- **Infrastructure** — реализует порты: `UptimeDbContext`, репозитории, HTTP-проверщик, JWT, BCrypt. Зависит от Application.
- **Api / Worker (Presentation)** — точки композиции. Знают все слои (чтобы собрать DI), но сами никому не нужны. Api — HTTP-интерфейс, Worker — цикл пингов.

Принципиальное решение: **без MediatR на MVP** — обычные классы-сценарии. CQRS-пайплайн (MediatR + behavior для валидации) — опциональная «красивость» на потом, на сдачу не влияет.

### Один файл — один тип

Договорённость по всему решению: **на файл ровно один класс, интерфейс, record, enum или struct**; имя файла = имя типа, namespace повторяет путь папки (с точностью до `backend/src`/`backend/tests`). Даже приватные типы-детали реализации (`Scope`, `ActionDisposable`, `ScopeStack`) живут отдельными файлами, а группы родственных типов разложены по подпапкам (`Monitors/Dtos`, `Monitors/UseCases`, `Persistence/Configurations`, `Hosting/Logging`).

Исключения — только генерируемый инструментами код: `Persistence/Migrations/*` (EF), top-level `Program.cs` у Api/Worker (компилятор сам создаёт класс `Program`) и `backend/scripts/TestRunner/Program.cs`. Готовый однострочник-проверка — в README, §2.

### Дерево решения

```
sre/                                  # корень репозитория (одна кодовая база — фактор I)
├─ backend/                           # ВСЯ серверная часть: решение, код, тесты, инструменты бека
│  ├─ Pingboard.sln
│  ├─ Directory.Packages.props        # центральные версии NuGet (фактор II)
│  ├─ scripts/TestRunner/             # in-process раннер тестов (обход падения VSTest в песочнице)
│  ├─ src/
│  │  ├─ Pingboard.Domain/            # ЯДРО — без внешних зависимостей
│  │  │  ├─ Entities/                 #   Monitor, CheckResult, User
│  │  │  ├─ Common/                   #   Entity<TId>, MonitorRules, ProbeOutcome
│  │  │  └─ DomainException.cs        #   + DomainValidationException, NotFoundException
│  │  ├─ Pingboard.Application/       # зависит только от Domain
│  │  │  ├─ Abstractions/             #   ПОРТЫ: IMonitorRepository, ICheckRepository,
│  │  │  │                            #         IUserRepository, IProbeService,
│  │  │  │                            #         IPasswordHasher, ITokenService, IUnitOfWork,
│  │  │  │                            #         ICurrentUser — по одному на файл
│  │  │  ├─ Monitors/                 #   MonitorMapper, MonitorStatusSummary
│  │  │  │  ├─ Dtos/                  #           MonitorDto, Create/UpdateMonitorRequest, ...
│  │  │  │  ├─ Options/               #           MonitorsOptions
│  │  │  │  └─ UseCases/              #           Create/List/Get/Update/Delete/GetChecks
│  │  │  ├─ Probing/                  #   RunDueChecks (сценарий воркера) + Options/
│  │  │  ├─ Auth/                     #   Register, Login + Dtos/
│  │  │  ├─ Common/                   #   ValidationFailedException, ForbiddenException
│  │  │  ├─ Composition/              #   MissingCurrentUser
│  │  │  └─ Validation/               #   свои валидаторы (FluentValidation недоступен оффлайн)
│  │  ├─ Pingboard.Infrastructure/    # реализует порты Application
│  │  │  ├─ Persistence/              #   UptimeDbContext, NamingConventions
│  │  │  │  ├─ Configurations/        #           по конфигурации на сущность
│  │  │  │  └─ Migrations/            #           EF-миграции
│  │  │  ├─ Repositories/             #   Monitor/Check/UserRepository, UnitOfWork
│  │  │  ├─ Probing/                  #   HttpProbeService (IHttpClientFactory) + Options/
│  │  │  ├─ Security/                 #   JwtTokenService, Pbkdf2PasswordHasher + Options/
│  │  │  └─ DependencyInjection.cs    #   AddInfrastructure(...) — единственный публичный API
│  │  ├─ Pingboard.Api/               # PRESENTATION: веб-точка входа
│  │  │  ├─ Program.cs                #   композиция: env → DI → middleware → endpoints
│  │  │  ├─ Endpoints/                #   AuthEndpoints, MonitorEndpoints, HealthEndpoints
│  │  │  ├─ Infrastructure/           #   ErrorHandling, ExceptionStatusMapper, JWT, JwtUserIdProvider
│  │  │  └─ appsettings.Development.json   #   только dev-умолчания; секреты — только env (фактор III)
│  │  └─ Pingboard.Worker/            # ВТОРОЙ PROCESS TYPE: пингер
│  │     ├─ Program.cs
│  │     ├─ DueCheckWorker.cs         #   BackgroundService-цикл
│  │     ├─ Options/WorkerOptions.cs
│  │     └─ Hosting/                  #   мини-хост + Logging/ + Configuration/
│  └─ tests/
│     ├─ Pingboard.Domain.Tests/      # Entities/ — инварианты сущностей
│     └─ Pingboard.Application.Tests/ # UseCases/ + Fakes/ — сценарии на фейках портов
│
├─ frontend/                          # React SPA (структура в §7)
├─ deploy/
│  ├─ Dockerfile.api                  # multi-stage: build → aspnet:10.0 (Api+Worker в одном образе)
│  ├─ Dockerfile.web                  # node:22 build → nginx:alpine
│  ├─ nginx.conf
│  ├─ docker-compose.yml
│  └─ docker-compose.prod.yml         # переопределения для «прода» на VPS
├─ scripts/                           # build, build-local-feed, run-tests, smoke-api — инструменты репозитория
├─ Directory.Build.props              # общий TargetFramework, warnings as errors (на ВСЕ проекты, вкл. backend)
├─ NuGet.config                       # оффлайн-фид .packages (путь относительный — рядом, в корне)
├─ .env.example                       # шаблон конфигурации (фактор III)
├─ .gitignore
└─ README.md                          # описание + таблица 12 факторов для отчёта
```

`Directory.Build.props` и `NuGet.config` лежат в корне намеренно: MSBuild и NuGet ищут их вверх по дереву от проекта, поэтому из корня они покрывают и `backend/**`, и `backend/scripts/TestRunner`; источник `.packages` в `NuGet.config` — относительный путь от каталога конфига (один оффлайн-фид на репозиторий). Пути внутри `backend/Pingboard.sln` считаются от каталога самого `.sln`, поэтому там по-прежнему `src\…` и `tests\…`.

### Ключевые типы

**Domain**

- `Monitor`: `Id (Guid)`, `OwnerId`, `Name` (1–100), `Url` (absolute http/https), `IntervalSeconds` (10..86400), `Enabled`, `CreatedAt/UpdatedAt`, `LastCheckedAt?`, `LastOk?`.
  Фабрика `Monitor.Create(...)` валидирует себя сама; `RecordCheck(...)` — единственный способ изменить статусные поля.
- `CheckResult`: `Id (long)`, `MonitorId`, `CheckedAt`, `Ok`, `StatusCode?`, `LatencyMs?`, `Error?`; фабрика `FromProbe(...)`.
- `User`: `Id`, `Email` (unique), `PasswordHash`, `CreatedAt`.

**Application — порты**

```csharp
public interface IMonitorRepository {
    Task AddAsync(Monitor m, CancellationToken ct);
    Task<Monitor?> GetAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Monitor>> ListAsync(Guid ownerId, CancellationToken ct);
    Task<IReadOnlyList<Monitor>> ListDueAsync(DateTimeOffset now, int batch, CancellationToken ct);
    void Remove(Monitor m);
}
public interface ICheckRepository {
    Task AddRangeAsync(IEnumerable<CheckResult> checks, CancellationToken ct);
    Task<IReadOnlyList<CheckResult>> HistoryAsync(Guid monitorId, DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken ct);
    Task<double?> UptimeRatio24hAsync(Guid monitorId, CancellationToken ct);   // 0..1 или null, если данных нет
}
public interface IProbeService {
    Task<ProbeResult> CheckAsync(string url, CancellationToken ct);            // ok, statusCode, latencyMs, error
}
public interface IUserRepository { /* AddAsync, GetByEmailAsync */ }
public interface IPasswordHasher { string Hash(string password); bool Verify(string password, string hash); }
public interface ITokenService  { string Issue(Guid userId); }
public interface IUnitOfWork     { Task SaveChangesAsync(CancellationToken ct); }
```

Время — через `System.TimeProvider` (в BCL): сценарии тестируются без мохов часов.

## 4. Модель данных

Схема генерируется EF-миграциями; целевое состояние:

```sql
CREATE TABLE users (
  id            uuid PRIMARY KEY,
  email         text NOT NULL UNIQUE,
  password_hash text NOT NULL,
  created_at    timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE monitors (
  id               uuid PRIMARY KEY,
  user_id          uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  name             text NOT NULL,
  url              text NOT NULL,
  interval_seconds int  NOT NULL DEFAULT 60,
  enabled          boolean NOT NULL DEFAULT true,
  last_checked_at  timestamptz,          -- состояние воркера живёт в БД, не в памяти (фактор VI)
  last_ok          boolean,
  created_at       timestamptz NOT NULL DEFAULT now(),
  updated_at       timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_monitors_user ON monitors(user_id);

CREATE TABLE checks (
  id          bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  monitor_id  uuid NOT NULL REFERENCES monitors(id) ON DELETE CASCADE,
  checked_at  timestamptz NOT NULL,
  ok          boolean NOT NULL,
  status_code int,
  latency_ms  int,
  error       text
);
CREATE INDEX ix_checks_monitor_time ON checks(monitor_id, checked_at DESC);
```

Пояснения:

- `last_checked_at`/`last_ok` денормализованы в `monitors`, чтобы список на дашборде читался одним дешёвым запросом без агрегата по `checks`.
- `uptime24h` считается на лету: `count(*) FILTER (WHERE ok) / count(*)` за последние 24 ч — кэшировать не нужно на таких объёмах.
- `ON DELETE CASCADE` — при удалении монитора история уходит сама.
- Маппинг — Fluent API в Infrastructure; в Domain никаких EF-атрибутов.

## 5. Контракт REST API

| Метод | Путь | Что делает | Auth |
|---|---|---|---|
| POST | `/api/auth/register` | создать аккаунт `{email, password}` → 201 | — |
| POST | `/api/auth/login` | `{email, password}` → `{accessToken}` | — |
| GET | `/api/monitors` | список с текущим статусом и `uptime24h` | Bearer |
| POST | `/api/monitors` | создать монитор → 201 + `MonitorDto` | Bearer |
| GET | `/api/monitors/{id}` | детали монитора | Bearer |
| PATCH | `/api/monitors/{id}` | частичное обновление (name/url/interval/enabled) | Bearer |
| DELETE | `/api/monitors/{id}` | удалить → 204 | Bearer |
| GET | `/api/monitors/{id}/checks?from&to&limit=500` | история проверок | Bearer |
| GET | `/healthz` | liveness: процесс жив | — |
| GET | `/readyz` | readiness: `SELECT 1` до Postgres | — |
| GET | `/metrics` | экспорт Prometheus | — |

Пример `MonitorDto`:

```json
{
  "id": "0f1e…", "name": "Мой блог", "url": "https://example.com",
  "intervalSeconds": 60, "enabled": true,
  "lastOk": true, "lastCheckedAt": "2026-09-20T10:00:05Z",
  "uptime24h": 0.997
}
```

- Ошибки — RFC 7807 ProblemDetails: 400 (валидация), 401, 403 (чужой монитор), 404.
- OpenAPI: встроенный `AddOpenApi()` + Swashbuckle (или Scalar) для UI — Swagger удобен для проверки CRUD до готового фронта.
- Порядок с авторизацией: в M1–M3 API работает от сид-пользователя (`demo@pingboard.local`, создаётся сид-скриптом; `ownerId` из конфига `Auth__DefaultUserId`). Веха M4 включает `[Authorize]` и берёт `ownerId` из JWT-claims — фронт добавляет экран логина.

## 6. Воркер (второй process type)

```csharp
// Pingboard.Worker — DueCheckWorker : BackgroundService
protected override async Task ExecuteAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();   // scoped DbContext на итерацию
            await scope.ServiceProvider.GetRequiredService<RunDueChecks>().ExecuteAsync(ct);
        }
        catch (Exception ex) { _log.LogWarning(ex, "tick failed"); }    // тик упал — цикл живёт (фактор IX)
        await Task.Delay(TimeSpan.FromSeconds(_tickSeconds), ct);
    }
}
```

`RunDueChecks` (сценарий в Application, воркер про HTTP и БД не знает):

1. `ListDueAsync(now, batch: 100)` — включённые мониторы, у которых `last_checked_at + interval <= now` (или ещё не проверялись).
2. `Parallel.ForEachAsync` с `MaxDegreeOf = Worker__MaxParallel`:
   `IProbeService.CheckAsync(url)` — **HEAD**, при 405/501 → повтор **GET**, таймаут `Probe__TimeoutMs`; латентность по Stopwatch.
3. Пишем `CheckResult` + `monitor.RecordCheck(...)`; `SaveChangesAsync` — одна транзакция на батч.

Свойства, которые и есть суть 12 факторов:

- весь «планировщик» — это запрос к БД: воркер **stateless**, рестарт/убийство в любой момент ничего не ломает (VI, IX);
- несколько воркеров дадут дубли проверок — фиксится `FOR UPDATE SKIP LOCKED` в `ListDueAsync` (расширение №6 в §12, для MVP один воркер);
- SIGTERM → `stoppingToken` срабатывает, итерация доигрывается, процесс выходит чисто (`HostOptions.ShutdownTimeout = 10s`).

## 7. Фронтенд (React + Vite)

```
frontend/
├─ index.html
├─ vite.config.ts          # dev-прокси: /api → http://localhost:8080
└─ src/
   ├─ main.tsx             # QueryClientProvider + Router
   ├─ api/
   │  ├─ client.ts         # axios: Bearer-интерсептор, 401 → на логин
   │  ├─ auth.ts           # login/register
   │  └─ monitors.ts       # CRUD + история, типы ответов
   ├─ hooks/               # useAuth, useMonitors (refetchInterval: 10_000), useChecks
   ├─ pages/               # LoginPage, DashboardPage, MonitorDetailPage
   ├─ components/          # Layout, StatusBadge, UptimeBar, Sparkline, MonitorForm
   └─ styles.css
```

Экраны:

- **Dashboard** — таблица: имя, URL, статус-бейдж (up/down/paused), UptimeBar за 24 ч (24 сегмента-часа: зелёный/красный/серый, как у UptimeRobot — на чистых div/SVG), последняя задержка, время проверки; кнопки: добавить, редактировать, пауза, удалить.
- **MonitorDetail** — спарклайн задержек (recharts или свой SVG ~30 строк) + таблица последних проверок.
- **Login/Register** — добавляются в M4.

«Живость» дашборда на MVP — поллинг через `refetchInterval` в TanStack Query; WebSocket/SSE — расширение.

## 8. Конфигурация — только env (фактор III)

| Переменная | Пример | Смысл |
|---|---|---|
| `ConnectionStrings__Default` | `Host=postgres;Database=pingboard;Username=pingboard;Password=…` | Postgres (фактор IV) |
| `Jwt__Secret` | `openssl rand -base64 48` | подпись HS256 (≥ 32 байта) |
| `Jwt__ExpiresMinutes` | `120` | срок жизни access-токена |
| `Probe__TimeoutMs` | `5000` | таймаут HTTP-проверки |
| `Worker__TickSeconds` | `5` | период цикла воркера |
| `Worker__MaxParallel` | `8` | параллелизм проверок |
| `Cors__Origins` | `http://localhost:5173` | источники для CORS |
| `Auth__DefaultUserId` | guid сид-пользователя | только до вехи M4 |
| `ASPNETCORE_URLS` | `http://+:8080` | порт (фактор VII) |
| `ASPNETCORE_ENVIRONMENT` | `Development` / `Production` | окружение |

- `__` (двойное подчёркивание) — .NET-нотация для вложенных ключей: `Worker:MaxParallel` → `Worker__MaxParallel`.
- Options-паттерн с проверкой на старте: `AddOptions<WorkerOptions>().BindConfiguration("Worker").ValidateOnStart()` — кривой конфиг падает сразу, а не посреди работы (fail-fast).
- `.env` — только для локального docker compose, в `.gitignore`; в репозитории — `.env.example`.

## 9. Docker и compose

```dockerfile
# deploy/Dockerfile.api — ОДИН образ для api и worker (фактор VIII: один релиз, разные команды)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish backend/src/Pingboard.Api -c Release -o /app
RUN dotnet publish backend/src/Pingboard.Worker -c Release -o /app    # обе точки входа в одном образе

FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*  # для healthcheck
WORKDIR /app
COPY --from=build /app ./
EXPOSE 8080
CMD ["dotnet", "Pingboard.Api.dll"]     # worker переопределит command в compose
```

```dockerfile
# deploy/Dockerfile.web
FROM node:22-alpine AS build
WORKDIR /frontend
COPY frontend/package*.json ./
RUN npm ci
COPY frontend/ .
RUN npm run build

FROM nginx:alpine
COPY deploy/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /frontend/dist /usr/share/nginx/html
```

```nginx
# deploy/nginx.conf
server {
  listen 80;
  location /api/ { proxy_pass http://api:8080; }
  location /     { root /usr/share/nginx/html; try_files $uri /index.html; }
}
```

```yaml
# deploy/docker-compose.yml (значения из .env)
services:
  postgres:
    image: postgres:18-alpine
    environment: { POSTGRES_USER: ${POSTGRES_USER}, POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}, POSTGRES_DB: ${POSTGRES_DB} }
    volumes: [pgdata:/var/lib/postgresql/data]
    healthcheck: { test: ["CMD-SHELL", "pg_isready -U $${POSTGRES_USER}"], interval: 5s, retries: 12 }
    # порт наружу не публикуем — только внутри сети compose

  api:
    build: { context: ., dockerfile: deploy/Dockerfile.api }
    env_file: .env
    depends_on: { postgres: { condition: service_healthy } }
    healthcheck: { test: ["CMD", "curl", "-fs", "http://localhost:8080/healthz"], interval: 10s }

  worker:
    build: { context: ., dockerfile: deploy/Dockerfile.api }
    command: ["dotnet", "Pingboard.Worker.dll"]      # тот же образ — второй process type
    env_file: .env
    depends_on: { postgres: { condition: service_healthy } }

  web:
    build: { context: ., dockerfile: deploy/Dockerfile.web }
    ports: ["8080:80"]
    depends_on: [api]

volumes: { pgdata: {} }
```

**Миграции** — два режима:

1. **MVP (M0):** `MigrateOnStart=true` — Api при старте выполняет `db.Database.Migrate()`. Просто и достаточно для учебного стенда.
2. **Канонично (M5):** `dotnet ef migrations bundle` при сборке → самодостаточный `pingboard-migrate`; в compose one-off сервис `migrate`, остальные ждут `condition: service_completed_successfully`. Это заодно живая демонстрация факторов V (release) и XII (admin-процесс) — в отчёте можно показать оба режима и объяснить разницу.

## 10. Двенадцать факторов → конкретные решения

| # | Фактор | Как в проекте |
|---|---|---|
| I | Codebase | один git-репозиторий (бек + фронт + deploy); dev-стенд и «прод» — два deploy'я одной кодовой базы |
| II | Dependencies | всё объявлено и запинено: `Directory.Packages.props` (центральные версии), `packages.lock.json`; в рантайм-образе нет SDK и глобальных пакетов |
| III | Config | все настройки из env (таблица §8); `.env` не в git, в репо `.env.example`; секреты не в `appsettings` |
| IV | Backing services | Postgres (и позже SMTP/Telegram) «прикреплены» env-строками; смена инстанса = смена переменной, код не трогаем |
| V | Build, release, run | build = multi-stage Dockerfile; release = тег образа + прогон миграций (bundle); run = compose (потом k8s) |
| VI | Processes | stateless: JWT вместо серверных сессий; «расписание» воркера — данные в БД, не таймеры в памяти |
| VII | Port binding | Kestrel слушает `ASPNETCORE_URLS=http://+:8080`; приложение самодостаточно |
| VIII | Concurrency | web и worker — два process types из одного образа; api скейлится горизонтально: `docker compose up --scale api=2` |
| IX | Disposability | SIGTERM → graceful shutdown (воркер доигрывает итерацию, `ShutdownTimeout=10s`); быстрые healthcheck'и |
| X | Dev/prod parity | локально крутятся те же образы, что на VPS; dev от prod отличается только env |
| XI | Logs | только stdout/stderr, JSON (`AddJsonConsole`); никаких файлов; собирает Docker (потом Loki) |
| XII | Admin processes | `dotnet ef migrations add` / `bundle`, сид-скрипт, `pingboard-migrate` — one-off процессы, не «фоновые фичи» приложения |

Плюс SRE-детали сверх факторов: `/healthz` (liveness) отдельно от `/readyz` (readiness с пингом БД), `/metrics`, CorrelationId в логах, структурные ошибки ProblemDetails.

## 11. Вехи

**M0. Скелет — ≈ полдня**
- [ ] git init, sln, 5 проектов, правило зависимостей собирается
- [ ] Dockerfile.api + compose (postgres/api/web-заглушка), первая EF-миграция, `MigrateOnStart`
- [ ] сид-пользователь, `/healthz`
- Готово, когда: `docker compose up` → все сервисы зелёные, `GET /healthz` → 200.

**M1. CRUD мониторов — 1 вечер**
- [ ] Domain: Monitor/CheckResult + валидация в фабриках
- [ ] Application: 5 сценариев + валидаторы + порты
- [ ] Infrastructure: DbContext, Fluent-конфигурации, миграция, репозитории
- [ ] Api: endpoints + Swagger UI
- Готово, когда: полный CRUD через Swagger, строки видны в psql.

**M2. Воркер и история — 1 вечер**
- [ ] `HttpProbeService` (HEAD→GET, таймаут, латентность)
- [ ] `RunDueChecks` + сервис `worker` в compose
- [ ] `GET /api/monitors/{id}/checks`, `uptime24h` в списке
- Готово, когда: добавил URL → через минуту в `checks` растут записи.

**M3. React — 1–2 вечера**
- [ ] Vite-каркас, axios-клиент, роутер, TanStack Query
- [ ] Dashboard с поллингом 10 с + UptimeBar
- [ ] Форма создать/редактировать, пауза/удаление
- [ ] Страница монитора: спарклайн + последние проверки
- Готово, когда: приложение end-to-end без Swagger. **(M0–M3 — минимальная сдача)**

**M4. JWT-авторизация — 1 вечер (можно отложить)**
- [ ] register/login, `[Authorize]`, `ownerId` из claims
- [ ] изоляция по владельцу (403 на чужой монитор), экран логина на фронте
- Готово, когда: два пользователя не видят мониторы друг друга.

**M5. SRE-полировка — ≈ полдня**
- [ ] `/readyz`, `/metrics`, JSON-логи + CorrelationId
- [ ] graceful shutdown проверен (`docker compose stop` — без обрыва ошибок)
- [ ] migrate-bundle вместо MigrateOnStart
- [ ] пара юнит-тестов (валидация Monitor, `RunDueChecks` на фейках)
- [ ] README: запуск, env-переменные, таблица 12 факторов
- Готово, когда: таблицу факторов из §10 можно вставлять в отчёт без изменений.

## 12. Расширения по SRE-темам (потолок роста)

| # | Расширение | Что прокачивает |
|---|---|---|
| 1 | Алертинг при падении: Telegram/email, анти-флаппинг (N неудач подряд) | фоновые джобы, интеграции; тема «алертинг и дежурства» |
| 2 | Prometheus + Grafana (compose-профайл): метрики самого приложения, свои counters `pingboard_checks_total{result}` | наблюдаемость, дашборды, alert-правила |
| 3 | OpenTelemetry → OTLP → Jaeger/Tempo | распределённый трейсинг |
| 4 | CI/CD: GitHub Actions (build + test + push в GHCR) → deploy на VPS по SSH | конвейер доставки |
| 5 | Нагрузочный тест k6 против API | перфоманс-инженерия |
| 6 | `FOR UPDATE SKIP LOCKED` в `ListDueAsync` → несколько реплик воркера | конкурентность, SQL |
| 7 | Ретеншн: воркер чистит `checks` старше N дней | эксплуатация данных |
| 8 | Публичная статус-страница (без auth) | продукт |
| 9 | Kubernetes-манифесты/Helm: Deployment api+worker, probes, HPA | контейнеры в бою |
| 10 | Rate limiting (встроенный `System.Threading.RateLimiting`) | защита API |

## 13. Грабли чистой архитектуры (чек-лист самопроверки)

- Domain без EF-атрибутов: маппинг только Fluent API в Infrastructure.
- Application не знает про EF, HttpClient, JWT, HttpContext — только свои порты.
- Репозитории наружу не отдают `IQueryable` (утечка инфраструктуры) — только сущности/DTO и готовные списки.
- Program.cs — только композиция; вся логика в сценариях, endpoints — тонкие.
- API-DTO ≠ доменные сущности: наружу только DTO.
- Api ссылается на Infrastructure только ради вызова `AddInfrastructure()`.
- Валидация — в Application (FluentValidation), не в endpoints.
- Все `DateTime` — UTC (`TimeProvider.GetUtcNow()`): Npgsql требует UTC для `timestamptz`, иначе ловятся коварные сдвиги часовых поясов.

## 14. Сценарий демонстрации на защите

1. `docker compose up --build` → дашборд открывается на `:8080`.
2. Добавить два монитора: живой (`https://example.com`) и падающий (`https://httpstat.us/500`) → бейджи green/red, у второго растёт downtime.
3. Открыть страницу монитора: спарклайн задержек, история проверок.
4. `docker compose restart worker` → ничего не потерялось, чеки продолжаются → **факторы VI/IX**.
5. `docker compose up --scale api=2` → приложение работает без изменений кода → **фактор VIII**.
6. `docker compose logs -f api` → JSON-логи в stdout → **фактор XI**.
7. `docker compose stop api` → в логах graceful shutdown → **фактор IX**.
8. Показать `.env.example` и место чтения конфига → **факторы III/IV**.

## 15. Ссылки

- 12-факторный манифест: https://12factor.net/ru/ (есть официальный русский перевод)
- Clean Architecture, референс-шаблон: https://github.com/jasontaylordev/CleanArchitecture
- EF Core migrations bundle: https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying?tabs=dotnet-core-cli#bundles
- Minimal APIs: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis
- Вдохновение по фичам (self-hosted uptime-монитор): https://github.com/louislam/uptime-kuma
- Метрики: https://github.com/prometheus-net/prometheus-net
