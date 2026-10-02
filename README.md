# Pingboard

Uptime-монитор для SRE-курса (мини-UptimeRobot): пользователь добавляет URL, фоновый воркер по расписанию делает HTTP-проверки, результаты пишутся в PostgreSQL, дашборд показывает статус, uptime и историю задержек.

Стек: **.NET 10** + Clean Architecture (Domain / Application / Infrastructure / Api / Worker), **EF Core 10 + Npgsql**, **PostgreSQL**, в перспективе React (Vite + TS).

> Этот репозиторий — **каркас бэкенда (M0)**: проекты, зависимости, правило зависимостей, ключевые интерфейсы-порты, DI-корень, сущности домена, миграция, тесты. Бизнес-логика проработана ровно настолько, чтобы решение собиралось, тесты проходили, а Api и воркер запускались.

---

## 1. Состояние

| Что | Статус |
|---|---|
| Решение и 5 проектов + 2 тестовых | ✅ собирается, 0 предупреждений |
| Центральные версии NuGet (`Directory.Packages.props`) | ✅ 23 пакета (версии в одном месте, в `.csproj` версий нет) |
| Lock-файлы NuGet (`packages.lock.json`, `RestorePackagesWithLockFile`) | ✅ по одному на проект: пинят транзитивные зависимости, рестор в CI/образе идёт `--locked-mode` |
| Доменные сущности и инварианты (`Monitor`, `CheckResult`, `User`) | ✅ с тестами (32) |
| Порты Application + сценарии (CRUD мониторов, цикл проверок, auth) | ✅ с тестами |
| Infrastructure: DbContext, репозитории, HTTP-пробер, JWT, хеш паролей | ✅ |
| Api: DI-корень, endpoints, health, OpenAPI | ✅ smoke-проверен |
| Worker: цикл пингов, JSON-логи, graceful shutdown | ✅ smoke-проверен |
| **JWT-авторизация (M4)**: защита маршрутов, владелец из claim `sub` | ✅ с тестами |
| EF-миграция `InitialCreate` | ✅ |
| deploy: Dockerfile.api/web, compose, nginx | ✅ есть; пошаговая инструкция — [DEPLOY.md](DEPLOY.md) (образы не собирались: нет доступа к реестру образов) |
| Тесты | ✅ 79 (32 Domain + 47 Application), `dotnet test` / in-process раннер |
| Замечания ревью M0 (ошибки/контракты/планировщик) | ✅ разобраны, см. §10 |
| `frontend/` (Vite + React + TS) | ✅ **M3**: дашборд с полосой доступности и поллингом, форма CRUD, страница монитора со спарклайном, логин/регистрация (M4) |

---

## 2. Структура

```
sre/
├─ backend/                       # вся серверная часть: решение, код, тесты, инструменты бека
│  ├─ Pingboard.sln
│  ├─ Directory.Packages.props    # центральные версии NuGet (фактор II)
│  │                              # + packages.lock.json рядом с каждым .csproj (транзитивные версии)
│  ├─ scripts/TestRunner/         # in-process раннер тестов (обход падения VSTest в песочнице)
│  ├─ src/
│  │  ├─ Pingboard.Domain/        # ЯДРО: ноль NuGet-зависимостей
│  │  │  ├─ Entities/             #   Monitor, CheckResult, User — по одному типу на файл
│  │  │  ├─ Common/               #   Entity<TId>, MonitorRules, ProbeOutcome
│  │  │  ├─ DomainException.cs    #   базовое нарушение инварианта
│  │  │  ├─ DomainValidationException.cs
│  │  │  └─ NotFoundException.cs
│  │  ├─ Pingboard.Application/   # сценарии + порты, зависит только от Domain
│  │  │  ├─ Abstractions/         #   порты: IMonitorRepository, ICheckRepository, IUserRepository,
│  │  │  │                        #   IUnitOfWork, IProbeService, IPasswordHasher, ITokenService,
│  │  │  │                        #   ICurrentUser, IDatabaseHealthProbe + IssuedToken (токен и его срок)
│  │  │  │                        #   — один интерфейс/тип на файл
│  │  │  ├─ Monitors/             #   MonitorMapper, MonitorStatusSummary
│  │  │  │  ├─ Dtos/              #   MonitorDto, Create/UpdateMonitorRequest, CheckPageDto, ...
│  │  │  │  ├─ Options/           #   MonitorsOptions
│  │  │  │  └─ UseCases/          #   Create/List/Get/Update/Delete/GetChecks + OwnedMonitor
│  │  │  ├─ Probing/              #   RunDueChecks (сценарий воркера) + Options/DueCheckOptions
│  │  │  ├─ Auth/                 #   RegisterUser, LoginUser + Dtos/
│  │  │  ├─ Validation/           #   IRequestValidator, ValidationResult, ValidationRunner,
│  │  │  │                        #   MonitorValidationRules, PasswordRules и валидаторы запросов
│  │  │  ├─ Common/               #   ValidationFailedException, ForbiddenException, UnauthorizedException
│  │  │  ├─ Composition/          #   MissingCurrentUser — заглушка порта ICurrentUser
│  │  │  └─ DependencyInjection.cs#   AddApplication()
│  │  ├─ Pingboard.Infrastructure/# адаптеры: EF Core, HttpClient, JWT, PBKDF2
│  │  │  ├─ Persistence/          #   UptimeDbContext, NamingConventions, DatabaseHealthProbe,
│  │  │  │                        #   design-time factory
│  │  │  │  ├─ Configurations/    #   User/Monitor/CheckResultConfiguration (по файлу на сущность)
│  │  │  │  └─ Migrations/        #   EF-миграции (генерируются инструментом)
│  │  │  ├─ Repositories/         #   MonitorRepository, CheckRepository, UserRepository, UnitOfWork
│  │  │  │                        #   (+ построители SQL: ListDue/HistoryForMany/UptimeRatios)
│  │  │  ├─ Probing/              #   HttpProbeService + Options/ProbeOptions
│  │  │  ├─ Security/             #   JwtTokenService, Pbkdf2PasswordHasher + Options/JwtOptions
│  │  │  └─ DependencyInjection.cs#   AddInfrastructure() — единственная точка входа слоя
│  │  ├─ Pingboard.Api/           # PRESENTATION: HTTP. Program.cs — DI-корень
│  │  │  ├─ Endpoints/            #   monitors, auth, health
│  │  │  └─ Infrastructure/       #   ErrorHandling + ExceptionStatusMapper + MappedError,
│  │  │                           #   ErrorResponseFormat (problem+json, realm), AuthRateLimiting,
│  │  │                           #   JWT-настройка, JwtUserIdProvider (владелец из claim sub), OpenAPI Bearer
│  │  └─ Pingboard.Worker/        # PRESENTATION: цикл пингов. Program.cs — DI-корень
│  │     ├─ DueCheckWorker.cs     #   scope на итерацию, защита цикла, пауза
│  │     ├─ Options/              #   WorkerOptions
│  │     └─ Hosting/              #   минимальный хост (см. §6.2)
│  │        ├─ Logging/           #   JSON-логи в stdout + ScopeStack
│  │        └─ Configuration/     #   мини-провайдеры: JSON-файл и env-переменные
│  └─ tests/
│     ├─ Pingboard.Domain.Tests/  # инварианты сущностей (Entities/)
│     └─ Pingboard.Application.Tests/# сценарии на фейках портов (UseCases/, Auth/, Fakes/),
│                                 # Api: маппинг ошибок (Api/), SQL-контракты БД (Infrastructure/)
├─ frontend/                      # SPA (M3): Vite + React + TS — структура и экраны в §3.4
│  ├─ index.html, vite.config.ts  #   dev-прокси /api → http://localhost:8080
│  ├─ package.json, package-lock.json
│  └─ src/
│     ├─ main.tsx, App.tsx        #   QueryClientProvider + AuthProvider + маршруты
│     ├─ api/                     #   client.ts (axios, Bearer, 401 → логин), auth.ts, monitors.ts, types.ts
│     ├─ hooks/                   #   useAuth, useMonitors (refetchInterval 10 с), useChecks
│     ├─ pages/                   #   LoginPage, DashboardPage, MonitorDetailPage
│     ├─ components/              #   Layout, RequireAuth, StatusBadge, UptimeBar, Sparkline,
│     │                           #   MonitorForm, FailureBanner, FieldErrorText
│     ├─ lib/format.ts            #   форматирование времени/задержек/uptime
│     └─ styles.css               #   одна тёмная тема на всё приложение
├─ scripts/                       # build, build-web, build-local-feed, run-tests, smoke-api — инструменты репозитория
├─ deploy/                        # Dockerfile.api, Dockerfile.web, compose (dev/prod), nginx.conf
├─ DEPLOY.md                      # развёртывание: dev-стенд, прод на VPS, TLS, бэкапы, откат
├─ Directory.Build.props          # net10.0, Nullable, WarningsAsErrors — на все проекты, вкл. backend
├─ NuGet.config                   # локальный оффлайн-фид .packages (см. §7)
├─ .dockerignore                  # контекст сборки образов: без node_modules/bin/obj и без секретов
└─ .env.example                   # вся конфигурация через окружение (фактор III)
```

Почему `Directory.Build.props` и `NuGet.config` остались в корне, а не в `backend/`: MSBuild и NuGet ищут их вверх по дереву от каждого проекта, поэтому из корня они достают и `backend/**`, и `backend/scripts/TestRunner`. Плюс источник `.packages` в `NuGet.config` задан относительным путём и разрешается относительно каталога самого конфига — так оффлайн-фид остаётся один на репозиторий. Пути внутри `backend/Pingboard.sln` тоже относительные: они считаются от каталога `.sln`, поэтому остались `src\…` и `tests\…`.

### Правило зависимостей

```
Domain  ←  Application  ←  Infrastructure
(ядро)     (use cases +      (адаптеры: EF Core,
            порты)           HttpClient, JWT, хеш)
                ↑                    ↑
          Api / Worker — точки композиции: env → DI → запуск
```

Соблюдение правила проверяется ссылками в `.csproj`:

* `Pingboard.Domain` — **ни одного** `PackageReference`;
* `Pingboard.Application` — только `ProjectReference` на Domain (+ abstractions BCL-пакетов);
* `Pingboard.Infrastructure` — `ProjectReference` на Application, реализации портов;
* `Api`/`Worker` — ссылаются на Infrastructure **только** чтобы вызвать `AddInfrastructure()`; ни один тип EF Core в их коде не упоминается.

### Один файл — один тип

В коде действует правило: **на файл ровно один класс, интерфейс, record, enum или struct** (вложенные приватные типы-детали реализации тоже выносятся в отдельный файл). Исключения — только генерируемые инструментами файлы:

* `backend/src/Pingboard.Infrastructure/Persistence/Migrations/*` — код EF-миграций;
* `backend/src/Pingboard.Api/Program.cs`, `backend/src/Pingboard.Worker/Program.cs` — top-level statements (компилятор сам создаёт из них `Program`);
* `backend/scripts/TestRunner/Program.cs` — встроенный in-process раннер тестов.

Правило проверяется просто и не требует анализаторов:

```powershell
# в каждом файле должна быть ровно одна строка объявления типа
Get-ChildItem -Recurse -File -Filter *.cs |
  Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' } |
  ForEach-Object {
    $n = (Select-String -Path $_.FullName -Pattern '^\s*(public|internal|private|protected|file)?\s*(sealed\s+|abstract\s+|static\s+|partial\s+|readonly\s+)*(class|interface|record|struct|enum)\s+\w+').Count
    if ($n -gt 1) { "{0}: {1} типов" -f $_.Name, $n }
  }
```

Имя файла совпадает с именем типа, а namespace повторяет путь папки (с точностью до `src/`/`tests/`): тип из `Monitors/Dtos/MonitorDto.cs` живёт в `Pingboard.Application.Monitors.Dtos`.

---

## 3. Быстрый старт

Полная инструкция по развёртыванию (dev-стенд, прод на VPS, TLS, бэкапы, обновление и откат,
диагностика) — [DEPLOY.md](DEPLOY.md). Ниже — короткая версия для разработки.

### 3.1 Локально, без Docker

```powershell
# 1) Postgres (например, контейнером)
docker run --name pingboard-pg -e POSTGRES_USER=pingboard -e POSTGRES_PASSWORD=pingboard `
  -e POSTGRES_DB=pingboard -p 5432:5432 -d postgres:18-alpine

# 2) Сборка и тесты. В песочнице DSH обязателен -ExecutionPolicy Bypass (см. §7).
powershell -ExecutionPolicy Bypass -File scripts/build.ps1
powershell -ExecutionPolicy Bypass -File scripts/run-tests.ps1

# 3) Api (миграции применятся сами: MigrateOnStart=true в Development)
dotnet run --project backend/src/Pingboard.Api -m:1
#    http://localhost:8080/healthz, /readyz, /scalar/v1, /openapi/v1.json

# 4) Worker — вторым процессом
dotnet run --project backend/src/Pingboard.Worker -m:1
```

### 3.2 Docker Compose (весь стенд)

```bash
cp .env.example .env        # заполнить пароли и Jwt__Secret (openssl rand -base64 48)
# Команды выполняются из корня репозитория. --env-file обязателен: подстановка ${...} в самом
# compose-файле ищет .env рядом с ним (в deploy/), а не в корне. Без флага POSTGRES_* в сервисе
# postgres остались бы умолчаниями, а строка подключения Api пришла бы из .env — и Api не смог
# бы подключиться к БД с другим паролем.
docker compose --env-file .env -f deploy/docker-compose.yml up --build
# api  → http://localhost:8080   (для curl и smoke-скриптов; на «проде» API_BIND=127.0.0.1:8080)
# frontend → http://localhost:8081   (профиль web: nginx со SPA и проксированием /api)
```

Проверить, что подстановка сработала (в выводе должны быть ваши значения, а не умолчания):

```bash
docker compose --env-file .env -f deploy/docker-compose.yml config | grep -E 'POSTGRES_|ConnectionStrings|API_BIND|WEB_BIND'
```

Профиль `web` оставлен отдельным намеренно: `docker compose --env-file .env -f deploy/docker-compose.yml up --build` собирает postgres + api + worker (то, что нужно для проверки API и воркера), а SPA поднимается явно — с добавлением `--profile web`. Воркер масштабируется независимо (`--scale worker=2` — фактор VIII: дублей проверок не будет, их не допускает захват в `ListDueAsync`), а чем ограничено масштабирование Api — в §6.5. Отдельный процесс миграций для «прода» — это `deploy/docker-compose.prod.yml` (профиль `migrate`, фактор XII):

```bash
docker compose --env-file .env -f deploy/docker-compose.yml -f deploy/docker-compose.prod.yml \
  --profile migrate run --rm migrate      # dotnet Pingboard.Api.dll --migrate: применить схему и выйти
```

### 3.3 Проверка каркаса

```powershell
powershell -ExecutionPolicy Bypass -File scripts/smoke-api.ps1
```

Скрипт поднимает Api **без БД** и проверяет `/healthz` → 200, `/readyz` → 503 (БД недоступна, но процесс жив), что в OpenAPI объявлены все 7 маршрутов, а также JWT-обвязку: `/api/monitors` без токена → 401, с валидным токеном → не 401, с чужой подписью → 401. В режиме без БД «валидный токен принят» виден как 503 — запрос прошёл аутентификацию и упал уже на Postgres.

Отдельно проверяется формат ошибок — то, на что опирается фронт: `Content-Type` ответа `application/problem+json` (RFC 7807), `WWW-Authenticate: Bearer realm="pingboard"` в 401, `traceId` в теле, `POST /api/auth/register` с битым JSON → **400**, а не 500, и срабатывание rate limit на `/api/auth/*` → **429** с `Retry-After` (скрипт поднимает Api с `RateLimit__RegisterPermitLimit=5`, чтобы не слать сотню запросов).

### 3.4 Фронтенд (SPA)

Api должен быть запущен на `:8080` (см. 3.1): dev-сервер Vite проксирует на него `/api`, поэтому отдельно настраивать CORS в браузере не нужно — клиент всегда ходит на свой origin.

```powershell
cd frontend
npm install --cache ../.npm-cache   # кэш npm внутрь репозитория, иначе EPERM (см. §7)
npm run dev:sandbox                 # http://localhost:5173
```

Суффикс `:sandbox` — из-за ограничений этой машины (Vite на Windows зовёт `exec("net use")`, а песочница запрещает `child_process` с pipe). На обычной машине и в Docker это `npm ci && npm run build`. Одной командой всё: `powershell -ExecutionPolicy Bypass -File scripts/build-web.ps1`.

Что на экранах:

* **Login** (`/login`) — вход и регистрация; по умолчанию подставлена демо-учётка стенда (`demo@pingboard.local` / `demo-password`), ошибки полей берутся из `ProblemDetails.errors`, а `traceId` показывается в баннере — по нему запрос ищется в JSON-логах Api;
* **Dashboard** (`/`) — таблица мониторов: статус, uptime 24 ч, полоса доступности (сегменты считает Api), последняя задержка, время проверки; создать/изменить/пауза/удалить; автоопрос каждые 10 с через `refetchInterval`;
* **Monitor detail** (`/monitors/:id`) — карточки статистики, спарклайн задержек за окно (1/6/24 ч, свой SVG), история проверок с кодами ответа и ошибками, пауза/возобновление.

Токен хранится в `localStorage` и подставляется интерсептором axios; 401 (кроме самих `auth/*`) чистит вход и уводит на логин. Клиентская защита маршрутов — удобство, а не безопасность: настоящая проверка на сервере (`RequireAuthorization` + владелец из claim `sub`).

---

## 4. API

Маршруты мониторов требуют заголовок `Authorization: Bearer <access-токен>`; `healthz`/`readyz` и `auth/*` — анонимные. Владелец данных берётся исключительно из claim `sub` проверенного токена: «пользователя по умолчанию» в коде нет.

| Метод | Путь | Назначение | Auth |
|---|---|---|---|
| `GET` | `/healthz` | liveness: процесс жив | — |
| `GET` | `/readyz` | readiness: `SELECT 1` до Postgres, иначе 503 | — |
| `POST` | `/api/auth/register` | создать аккаунт, вернуть access-токен | — |
| `POST` | `/api/auth/login` | вход, access-токен | — |
| `GET` | `/api/monitors` | список мониторов + uptime24h + полоса доступности | Bearer |
| `POST` | `/api/monitors` | создать монитор | Bearer |
| `GET` | `/api/monitors/{id}` | детали монитора | Bearer |
| `PATCH` | `/api/monitors/{id}` | частичное обновление (name/url/interval/enabled) | Bearer |
| `DELETE` | `/api/monitors/{id}` | удалить монитор вместе с историей | Bearer |
| `GET` | `/api/monitors/{id}/checks?from=&to=&limit=` | история проверок | Bearer |
| `GET` | `/openapi/v1.json`, `/scalar/v1` | описание и UI (схема Bearer объявлена); только в Development | — |

Ошибки — RFC 7807 `ProblemDetails` с media type `application/problem+json` и `traceId` в теле: 400 с разбивкой по полям (в том числе битый JSON — он ловится на биндинге тела и больше не превращается в 500), 401 (нет/просрочен токен, неверная пара email+пароль — с заголовком `WWW-Authenticate: Bearer realm="pingboard"`), 403 на чужой монитор, 404, 409 при прочем нарушении уникальности, 429 при превышении лимита на `/api/auth/*` (с `Retry-After`), 499 на отменённый запрос и 503, только если недоступна БД. Серверная SQL-ошибка (синтаксис, ограничение, права) — это 500: 503 в этом случае уводил бы дежурного на ложный алерт.

5xx пишутся один раз, с кодом и путём (категория `Pingboard.Api.Unhandled`). Собственный Error-лог `ExceptionHandlerMiddleware` выключен в `appsettings.json` — иначе каждая опечатка клиента (400/401) появляется в логах как авария со стектрейпом; вернуть его можно через `Logging__LogLevel__Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware`.

Чтобы получить токен на свежем стенде, войдите демо-учёткой, которую создаёт сид (`Auth__DefaultUserEmail` / `Auth__DefaultUserPassword`, по умолчанию `demo@pingboard.local` / `demo-password`):

```powershell
$token = (Invoke-RestMethod -Method Post -Uri http://localhost:8080/api/auth/login `
    -ContentType 'application/json' `
    -Body '{"email":"demo@pingboard.local","password":"demo-password"}').accessToken
Invoke-RestMethod -Uri http://localhost:8080/api/monitors -Headers @{ Authorization = "Bearer $token" }
```

---

## 5. Конфигурация

Только окружение (фактор III): `appsettings.json` содержит dev-умолчания **без секретов**, всё остальное — переменные вида `Worker__MaxParallel`. В [.env.example](.env.example) — рабочий набор стенда, включая ключи с безопасными дефолтами (`SeedOnStart`, `Probe__HealthyStatusCodes`, `Probe__UseHeadWithGetFallback`, `Logging__LogLevel__*`): они идут закомментированными, с пометкой «менять осознанно» и предупреждением про массивы в env (индексы подряд от нуля, без пустых значений). Строка подключения `ConnectionStrings__Default` обязательна, иначе процесс падает на старте с понятной ошибкой.

`Jwt__Secret` (≥ 32 байта, `openssl rand -base64 48`) — ключ подписи HS256. В Development его можно не задавать: будет сгенерирован эфемерный ключ на запуск (в логе — warning, токены не переживут рестарт). В любом другом окружении отсутствие валидного секрета останавливает старт: иначе API принимал бы токены, подписанные известным всем ключом. Ключа `Auth__DefaultUserId` в конфигурации больше нет: Id демо-учётки — константа в коде (`DemoUser.Id`), а владелец запроса всегда берётся из claim `sub`.

`Jwt__Secret` проверяется и валидатором Options (`JwtSecretValidator` в Infrastructure, ≥ 32 байта для HS256), поэтому «секрет из трёх букв» не доживает до выдачи токена. Сид демо-учётки запрещён вне Development (`SeedOnStart` + не-Development → отказ старта): пароль этой учётки известен из исходников.

`SeedOnStart`, `MigrateOnStart`, `Cors__Origins__*` — из §9 PLAN.md. Отдельно про миграции: `dotnet Pingboard.Api.dll --migrate` — админ-режим той же сборки, который применяет схему и выходит (веб-сервер не поднимается); им пользуется профиль `migrate` на «проде».

Секция `Monitors` отвечает за представление: окно uptime, число сегментов полосы, глубину истории на дашборде. `Probe` — таймаут, набор «здоровых» статусов и барьер SSRF (`Probe__AllowPrivateNetworks`, по умолчанию `false`). `Worker` — тик, размер батча, параллелизм и файл-пульс для healthcheck. `RateLimit` — сколько запросов к `/api/auth/*` разрешено с одного адреса за окно; политики раздельные (`LoginPermitLimit` — 20, `RegisterPermitLimit` — 10: регистрация ещё и пишет в БД). Старый общий ключ `RateLimit__AuthPermitLimit` продолжает работать как фолбэк, чтобы существующие `.env` и `scripts/smoke-api.ps1` не сломались. `ForwardedHeaders` — доверие `X-Forwarded-For` от прокси (по умолчанию выключено, см. §6.5).

Пароль ограничен 8..128 символами (`PasswordRules`, общие для регистрации и входа): нижняя граница — от подбора, верхняя — от «пароля» в мегабайт, на котором PBKDF2 считается заметное время.

---

## 6. Инженерные решения

### 6.1 Диаграмма слоёв и DI

* `AddApplication()` — сценарии + `TimeProvider` + заглушка `ICurrentUser`.
* `AddInfrastructure(configuration)` — `UptimeDbContext`, репозитории, `IProbeService`, JWT, хеш паролей, валидация Options на старте (`ValidateOnStart`).
* `AddJwtAuthentication()` (Api) — JWT Bearer (`MapInboundClaims = false`), 401/403 телами `ProblemDetails`, схема Bearer в OpenAPI.
* `AddAuthRateLimiting()` (Api) — лимит на `/api/auth/*` с отказом в том же формате ошибки (429 + `Retry-After`).
* `JwtUserIdProvider` — реализация `ICurrentUser`: владелец = claim `sub` проверенного токена.

Оба процесса (`Api`, `Worker`) собирают DI одинаково — это и есть «один код, два process type» (фактор VIII).

### 6.1.1 Авторизация: что именно защищено и как это проверено

Группа `/api/monitors` закрыта `RequireAuthorization()` — все шесть операций. `ICurrentUser` читает `sub` из токена и **не имеет запасного «пользователя по умолчанию»**: без валидного токена это 401, а не доступ к чужому аккаунту. Отдельно закрыт вопрос «монитор чужой» — сценарии сравнивают `OwnerId` с текущим пользователем и отдают 403.

Секрет подписи (`Jwt__Secret`) нужен и выдаче (`JwtTokenService`), и проверке (`AddJwtBearer`), поэтому берётся из одного места — конфигурации:

* **Development**: если секрет не задан, `JwtSecretProvisioning` генерирует эфемерный на запуск и пишет warning. Удобно для стенда; токены не переживают рестарт; при нескольких репликах Api ключ у каждой свой (`RandomNumberGenerator.GetBytes` в каждом процессе), поэтому стенду с `--scale api=2` нужен явный `Jwt__Secret` — иначе токен, выданный одной репликой, вторая отвергнет с 401;
* **остальные окружения**: без валидного секрета процесс падает на старте с понятной ошибкой. Молчаливая подпись «нулями» была бы дырой: такой ключ известен всем, и токены можно подделать.

Проверки, которые это фиксируют:

* `JwtTokenRoundTripTests` — токен от `JwtTokenService` принимается параметрами валидации Api, `sub` доходит без искажений, чужая подпись/чужой audience/истёкший токен отвергаются;
* `JwtUserIdProviderTests` — владелец берётся из `sub`, а при его отсутствии бросается 401 (в том числе когда HTTP-контекста нет вовсе);
* `JwtBearerSetupTests` — схема по умолчанию именно Bearer, ключ и audience совпадают с выдачей, маппинг claims выключен;
* `scripts/smoke-api.ps1` — на живом процессе: без токена 401, валидный токен принят, чужая подпись отвергнута.

### 6.2 Отступления от PLAN.md и почему

В этой среде **нет доступа к nuget.org** (TLS-ошибка), поэтому часть пакетов из §2 плана недоступна. Каркас собран на том, что есть в локальном кэше, и каждое отступление изолировано так, чтобы замена была механической:

| Пакет из плана | Чем заменено | Как вернуть |
|---|---|---|
| `FluentValidation` | свои валидаторы (`IRequestValidator<T>`, `ValidationRunner`), правила 1-в-1 ложатся на `RuleFor` | добавить пакет + переписать `Validation/*` |
| `BCrypt.Net-Next` | `Pbkdf2PasswordHasher` (PBKDF2-HMAC-SHA256, 210k итераций, `FixedTimeEquals`) | заменить класс в `AddInfrastructure` |
| `prometheus-net.AspNetCore` | `/metrics` пока нет, в `HealthEndpoints` оставлен TODO и точная команда подключения | добавить пакет + `app.MapMetrics()` |
| `Microsoft.Extensions.Hosting` | свой минимальный хост `Pingboard.Worker/Hosting/WorkerHost.cs` на `Hosting.Abstractions` (даёт `BackgroundService`, JSON-логи в stdout, SIGTERM/Ctrl+C) | удалить `Hosting/` и вернуть `Sdk="Microsoft.NET.Sdk.Worker"` |
| `...Configuration.Json`, `...Logging.Console` | мини-провайдеры в том же `Hosting/` (плоский JSON + env-переменные) | заменить на `AddJsonFile` / `AddJsonConsole` |

Плюс два отступления по инфраструктуре:

* репозитории и `ProblemDetails` вместо `dotnet ef database update` в контейнере — в рантайм-образе нет SDK, поэтому схему применяет админ-режим самой сборки: `dotnet Pingboard.Api.dll --migrate` (профиль `migrate` в `docker-compose.prod.yml`; self-contained bundle из §9 PLAN.md — по-прежнему M5);
* `MigrateOnStart`/`SeedOnStart` — dev-удобство из §9 плана, отключаются переменными окружения.

### 6.3 Ключевые порты

```csharp
IMonitorRepository   // AddAsync, GetAsync, ListAsync, ListDueAsync(now, batch), Remove
ICheckRepository     // AddRangeAsync, HistoryAsync, HistoryForManyAsync, UptimeRatioAsync,
                     // UptimeRatioForManyAsync, UptimeBucketsForManyAsync (агрегат по сегментам полосы)
IUserRepository      // AddAsync, GetByEmailAsync, GetAsync
IUnitOfWork          // SaveChangesAsync  — одна транзакция на сценарий
IProbeService        // CheckAsync(url) → ProbeOutcome; исключений не бросает, кроме отмены
IPasswordHasher      // Hash / Verify + DummyHash (проверка пароля при отсутствующем пользователе)
ITokenService        // Issue(userId) → IssuedToken (токен и момент его истечения)
ICurrentUser         // UserId — claim sub проверенного токена (JwtUserIdProvider)
IDatabaseHealthProbe // CanConnectAsync — readiness, чтобы в Api не было типов EF Core
```

Наружу репозитории **не отдают `IQueryable`** — вся работа с БД остаётся в Infrastructure. Построители запросов внутри репозиториев (`ListDueQuery`, `HistoryForManyQuery`, `UptimeRatiosQuery`, `UptimeBucketsQuery`) — `internal static`: их SQL проверяется тестом без живой БД (`ToQueryString`), потому что планировщик и лимиты дашборда иначе видны только на Postgres.

`UptimeBucketsForManyAsync` — единственный метод порта с реализацией по умолчанию (пустой словарь). Это осознанный контракт, а не забывчивость: полоса доступности строится из агрегата по сегментам, но у сценариев дашборда есть фолбэк на историю, поэтому репозиторий, который агрегат не считает, продолжает работать — просто менее точно.

Пересечения базы и HTTP-слоя описаны явно: `ExceptionStatusMapper` (Api) различает гонку регистраций по имени UNIQUE-индекса `ux_users_email` — совпадение имени с моделью EF Core проверяется тестом `DatabaseContractTests`.

### 6.4 Схема БД (`InitialCreate`)

`users(id, email UNIQUE, password_hash, created_at)` → `monitors(id, user_id FK, name, url, interval_seconds, enabled, last_checked_at, last_ok, created_at, updated_at)` → `checks(id BIGSERIAL, monitor_id FK, checked_at, ok, status_code, latency_ms, error)`.

Индексы под конкретные запросы: `ix_monitors_due (enabled, last_checked_at)` — выборка просроченных воркером, `ix_checks_monitor_time (monitor_id, checked_at DESC)` — история и uptime, `ux_users_email`.

Все `DateTimeOffset` конвертируются в UTC (`timestamptz` + `Offset=0`) одним правилом в `UptimeDbContext`: иначе Npgsql падает на записи с локальным смещением.

### 6.5 Осознанные границы

Что сделано намеренно «не до конца» и почему — чтобы это не выглядело недосмотром:

* **SSRF закрыт по умолчанию, но остаётся ручка.** Адрес монитора задаёт пользователь, поэтому пробер ходит только на публичные адреса: `ProbeTargetGuard` (Infrastructure) резолвит DNS сам и соединяется лишь с разрешёнными IP — loopback, `10/8`, `172.16/12`, `192.168/16`, `169.254/16`, `100.64/10`, link-local, multicast и IPv6-эквиваленты отклоняются с внятной ошибкой в истории проверок. Разрешение и соединение в одном месте — потому и `SocketsHttpHandler.ConnectCallback`: проверка адреса перед подключением, а не после (иначе DNS rebinding сводит барьер на нет). Мониторинг внутренних сервисов (типовой сценарий uptime-монитора!) включается явно — `Probe__AllowPrivateNetworks=true` на доверенном стенде. Автопереходы отключены (`AllowAutoRedirect = false`): иначе барьер обходится редиректом на `127.0.0.1`, а зависший редирект-цикл съедает бюджет проверки.
* **Rate limit — в памяти процесса и по адресу соединения.** Счётчик живёт в процессе: при нескольких репликах Api каждый считает свои запросы, а за nginx все клиенты приходят с адресом прокси, то есть лимит становится общим на стенд. Первая половина лечится `ForwardedHeaders__Enabled=true` + явным списком доверенных прокси/сетей (только там, где Api доступен исключительно через свой прокси: иначе `X-Forwarded-For` подделывается и лимит обходится), вторая — распределённым счётчиком, это M5.
* **Масштабирование: реплики воркера безопасны, репликам Api мешает конфигурация.** Оба процесса stateless (фактор VI): расписание проверок — данные в БД, сессий в процессе нет, у Api нет фоновых сервисов. Воркер можно поднимать в нескольких репликах уже сейчас: `ListDueAsync` — это выборка кандидатов плюс захват условным `UPDATE … WHERE всё ещё просрочен`, и наружу отдаются только строки со штампом `now` этой итерации, поэтому сосед по тем же мониторам получит пустую выборку. Цена захвата — мониторы, захваченные процессом, который сразу умер, пропускают один интервал; `FOR UPDATE SKIP LOCKED` (расширение №6) убирает именно эту цену (и второй запрос на выборку), а не дубли. У Api три границы: он публикует фиксированный хост-порт (`API_BIND`, вторая реплика не забиндит 8080 — рецепт в DEPLOY.md §5.7), секрет подписи должен быть задан явно (иначе у каждой реплики свой эфемерный ключ, §6.1.1) и nginx резолвит `api:8080` один раз при старте, поэтому после `--scale api=2` нужен `nginx -s reload`. Плюс лимиты выше: счётчик у каждой реплики свой.
* **PII в логах.** Email не попадает в логи: неудачный вход логируется без него, а stdout собирается платформой навсегда (фактор XI).
* **Тайминг на логине.** Пароль проверяется всегда — для отсутствующего пользователя по хешу-заглушке (`IPasswordHasher.DummyHash`), чтобы «нет такого email» не отвечало быстрее.

---

## 7. Особенности этой машины

Подробный разбор ограничений среды и причин каждого из них — в [SANDBOX.md](SANDBOX.md). Коротко:

| Симптом | Причина | Что делать |
|---|---|---|
| `error NU1301: SSL connection could not be established` | TLS в .NET/Schannel не получает учётные данные под restricted-токеном (`SEC_E_NO_CREDENTIALS`); сеть при этом доступна — Node работает | восстановить пакеты в фид: `powershell -ExecutionPolicy Bypass -File scripts/build-local-feed.ps1` |
| «Ошибка сборки» без единой ошибки в логе | песочница запрещает параллельным узлам MSBuild общаться между собой | всегда `-m:1`: `dotnet build backend/Pingboard.sln -m:1`, `dotnet test ... -m:1` |
| `testhost` падает с `Win32Exception (5)` в `OpenProcess` | запрещено следить за родительским процессом | `powershell -ExecutionPolicy Bypass -File scripts/run-tests.ps1` (уходит на in-process раннер `backend/scripts/TestRunner`) |
| `... is not digitally signed. You cannot run this script` | политика выполнения `RemoteSigned`, скрипты не подписаны | `-ExecutionPolicy Bypass` перед `-File` |
| скрипт отработал, но отчёта `dotnet` нет | stdout нативной команды не сливается до `exit` | вывод прокачивается через `2>&1 \| Out-Host` (уже сделано в `scripts/`) |
| `npm error code EPERM ... npm-cache\_cacache\tmp` | кэш npm лежит в `%LOCALAPPDATA%`, вне workspace | `npm install --cache ../.npm-cache` (первый запуск), либо `scripts/build-web.ps1` |
| Vite: `spawn EPERM` в `optimizeSafeRealPathSync` | Vite на Windows зовёт `exec("net use")`, а `child_process` с pipe песочница запрещает | `npm run dev:sandbox` / `npm run build:sandbox` (`--configLoader native`) |

`NuGet.config` в репозитории указывает только на локальный фид `.packages` (в git его нет — восстанавливается скриптом). Чтобы снова ходить в интернет, добавьте в `NuGet.config` источник `nuget.org` (закомментирован рядом).

Полностью снять ограничения §1, §3, §4 и обеих строк про PowerShell можно переключением режима DSH на `danger-full-access` — командой `/permission` в чате или через **Настройки → Разрешения**; тогда изоляция команд отключается. Подробнее — [SANDBOX.md](SANDBOX.md#как-отключить-песочницу-целиком).

---

## 8. Разработка

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build.ps1     # restore/build, -m:1 подставляется сам
powershell -ExecutionPolicy Bypass -File scripts/run-tests.ps1 # тесты
powershell -ExecutionPolicy Bypass -File scripts/smoke-api.ps1 # smoke Api без БД
powershell -ExecutionPolicy Bypass -File scripts/build-web.ps1 # SPA: npm install + tsc + vite build
dotnet run --project backend/src/Pingboard.Api -m:1      # Api  → :8080
dotnet run --project backend/src/Pingboard.Worker -m:1   # Worker

cd frontend && npm run dev:sandbox                       # SPA → :5173 (прокси /api → :8080)

# новая миграция (сначала соберите Infrastructure, потом --no-build)
dotnet ef migrations add <Name> --project backend/src/Pingboard.Infrastructure --output-dir Persistence/Migrations --no-build
```

Тесты сценариев идут на фейках портов (`tests/Pingboard.Application.Tests/Fakes`) — без БД, HTTP и таймеров; время подменяется `TestTimeProvider`. Тесты Api (`Api/`) проверяют маппинг исключений в статусы, а `Infrastructure/` — сгенерированный SQL: живой Postgres этим не заменяется, но ловит именно те ошибки, которые иначе всплывают только на стенде.

У фронтенда тестов нет: проверяется `tsc --noEmit` в сборке (типы + контракты DTO) и живой дым по прокси — `/api/monitors` без токена через dev-сервер отдаёт 401 `problem+json`, `POST /api/auth/register` с плохим email — 400 с разбивкой по полям, а логин без поднятого Postgres — 503 с `traceId` (ровно то, что рисует баннер на экране).

---

## 9. Что дальше (по PLAN.md)

* **M3** — ✅ сделано: `frontend/` — Vite + React + TS, TanStack Query с поллингом 10 с, дашборд с полосой доступности, форма создать/изменить, пауза/удаление, страница монитора со спарклайном (свой SVG вместо recharts, PLAN.md §7) и историей проверок.
* **M4** — ✅ сделано и в бэкенде (`[Authorize]`, владелец из claim `sub`), и на фронте (экран входа/регистрации, `401 → логин`).
* **M5** — `/metrics`, migrate-bundle отдельным процессом, Testcontainers, распределённый rate limit, compose-стенд целиком (nginx) и README-скриншоты. Заодно фронт: SSE/WebSocket вместо поллинга, публичная статус-страница, тесты компонентов.
* **Проверка на живой БД** — по-прежнему главное непроверенное: сквозной сценарий «логин в SPA → создание монитора → воркер пишет `checks` → дашборд рисует полосу» не прогонялся, потому что на этой машине нет работающего Postgres (Docker Desktop не запущен, до реестра образов сети нет). Что удалось закрыть без БД: SQL планировщика, дашборда и агрегата полосы проверяется на сгенерированном запросе (`Infrastructure/DatabaseContractTests` + ручная проверка `ToQueryString` для захвата мониторов и группировки по сегментам), формат ошибок, 401/400/503 и rate limit — на живом процессе (`scripts/smoke-api.ps1`), а фронт — типизацией, сборкой и запросами через dev-прокси к живому Api (401/400/503 с реальными телами `problem+json`). Не проверено исполнение запросов самим Postgres: `ROW_NUMBER()` в истории, `COALESCE(..., '-infinity')` в планировщике, условный `UPDATE` захвата, `date_part('epoch', …)` в сегментах полосы, применение миграции и запись `checks` воркером; поведение `UnitOfWork` при удалённом мониторе закрыто только логикой (гонку с внешним `DELETE` на живом Postgres воспроизвести негде). Отдельно не проверена отрисовка SPA в настоящем браузере: браузера и Playwright в песочнице нет, поэтому вёрстка, спарклайн и полоса проверены только сборкой и разбором контрактов — глазами их стоит посмотреть первым же запуском `npm run dev`.

---

## 10. Ревью M0: что исправлено

Замечания ревью каркаса и то, чем они закрыты (все ссылки — на код и тесты в этом репозитории):

| # | Замечание | Исправление | Чем проверено |
|---|---|---|---|
| 1 | Битый JSON в теле → 500 и Error в логе | кейс `BadHttpRequestException` → 400 в `ExceptionStatusMapper`; Error-лог `ExceptionHandlerMiddleware` выключен | `ExceptionStatusMapperTests`, `smoke-api.ps1` (живой 400) |
| 2 | `expiresAt` в ответе login/register — момент выдачи | порт `ITokenService` возвращает `IssuedToken` (токен + срок), сценарии его не вычисляют | `JwtTokenRoundTripTests`, `LoginUserTests` |
| 3 | Любой `PostgresException` → 503 «БД недоступна» | 503 только для транзиентных (`PostgresException.IsTransient`); 23505 на `ux_users_email` → 400 по полю email, прочее 23505 → 409, остальные SQL-ошибки → 500 | `ExceptionStatusMapperTests`, `DatabaseContractTests` (имя индекса) |
| 4 | Content-Type ошибок `application/json` | один `ErrorResponseFormat` для всех, кто отдаёт ошибку: `application/problem+json`, заголовок ставится до тела | `smoke-api.ps1` (живой ответ) |
| 5 | Планировщик голодает: NULL в Postgres идёт в конец | `OrderBy(m => m.LastCheckedAt ?? DateTimeOffset.MinValue)` (COALESCE) + фейк повторяет то же выражение | `DatabaseContractTests`, `RunDueChecksTests` |
| 6 | `HistoryForManyAsync` не держит лимит «на монитор» | один запрос с `row_number() OVER (PARTITION BY monitor_id …)`; EF Core `GroupBy + Take` не транслирует, поэтому SQL явный | `DatabaseContractTests`, `ListMonitorsTests` |
| 7 | N+1 на дашборде: 2N+1 запросов | порт `UptimeRatioForManyAsync` — один `GROUP BY monitor_id` с `count(*) FILTER (WHERE ok)` | `DatabaseContractTests`, `ListMonitorsTests` (счётчик одиночных вызовов) |
| 8 | HEAD→GET делит общий таймаут | `HttpProbeService.AttemptAsync`: у каждой попытки свой CTS и свой бюджет `Probe__TimeoutMs` | — |
| 9 | Тайминг-утечка на логине и email в логе | `Verify` вызывается всегда (по `DummyHash`), email из лога убран | `LoginUserTests` |
| 10 | 401 от неверного пароля без `realm` | заголовок челленджа один на всех — `ErrorResponseFormat.BearerChallenge` | `ExceptionStatusMapperTests`, `smoke-api.ps1` |
| 11 | Тип EF Core в `HealthEndpoints` | порт `IDatabaseHealthProbe`, реализация в Infrastructure | правило «в Api нет EF Core» снова выполняется буквально |
| 12 | Док-комментарии «до M4» | комментарии `ICurrentUser`, `RegisterUser`, `AuthEndpoints`, `SeedDefaultUserAsync` переписаны | — |
| 13 | Дубли `WorkerOptions`/`DueCheckOptions` | батч и параллелизм только в `DueCheckOptions`, лог берёт значения оттуда же | — |
| 14 | `WorkerHost` ждёт остановку бесконечно | явный бюджет `WorkerHost.ShutdownTimeout` = 10 s, как у Api | — |
| 15 | `DomainValidationException` теряет имя поля | поле — отдельное свойство, маппер кладёт его ключом (дубль «Email: Email …» убран) | `ExceptionStatusMapperTests` |
| 16 | Нет rate limit на `/api/auth/*` и max-длины пароля | `AddAuthRateLimiting` (429 + `Retry-After` в формате ProblemDetails) и `PasswordRules` 8..128 | `smoke-api.ps1`, валидаторы регистрации/входа |
| 17 | SSRF «by design» | закрыт: `ProbeTargetGuard` + `SocketsHttpHandler.ConnectCallback` (только публичные адреса), opt-out `Probe__AllowPrivateNetworks=true` | — |

---

## 11. Ревью M1: что исправлено во втором проходе

Второй проход по бэкенду (после M1) и то, чем закрыто каждое замечание. Таблица про то же, что и §10, только про следующий срез: гонки воркера, контракты портов, согласованность дашборда, поведение под прокси.

| # | Замечание | Исправление | Чем проверено |
|---|---|---|---|
| 1 | `GET/POST /api/monitors` отдавал 404: маршрут объявлен как `"/"` | `MapGet("")` / `MapPost("")` — маршрут без завершающего слэша | `smoke-api.ps1` (в OpenAPI ровно 7 путей, включая `/api/monitors`) |
| 2 | Два воркера (или перекрывшиеся итерации) проверяли одни мониторы и писали дубли в историю | двухшаговый планировщик: выборка кандидатов + захват условным `UPDATE … WHERE всё ещё просрочен` (`ExecuteUpdateAsync`); результат — только реально захваченные строки | сгенерированный SQL проверен на `ToQueryString` (см. §6.3 о контрактных тестах) |
| 3 | Падение сохранения из-за удалённого монитора рушило весь батч | `UnitOfWork.SaveChangesAsync` убирает из трекинга результаты мониторов, которых в БД уже нет, и повторяет сохранение; прочие ошибки (в т.ч. UNIQUE на email) пробрасываются как раньше | существующие тесты сценариев (79/79) |
| 4 | `UptimeRatioAsync` — два `COUNT` подряд: между ними влезала проверка, и uptime мог быть > 1 | один агрегатный запрос (`UptimeRatiosQuery`), как и в пакетном варианте | `DatabaseContractTests`, `ListMonitorsTests` |
| 5 | Полоса доступности строилась из последних 500 проверок: на интервале 10 с это ~час из 24, 20 сегментов серые при uptime за всё окно | агрегат по сегментам одним `GROUP BY` (`UptimeBucketsQuery` / `UptimeBucketsForManyAsync`), номер сегмента считается по времени проверки; фолбэк на историю сохранён | SQL проверен на `ToQueryString`; тесты дашборда — на фолбэке (79/79) |
| 6 | `HistoryForManyAsync`/`GetMonitorChecks` не сообщали о том, что данные обрезаны лимитом | `CheckPageDto.HasMore` — запрос читает `limit + 1` строку, лишняя не отдаётся; валидация `limit`/`from`/`to` в сценарии | существующие тесты (клиентских правок не требует: SPA на тот момент лежал в пустом `web/`, сейчас это `frontend/`) |
| 7 | `CreateMonitor`/`UpdateMonitor` не учитывали время в полосе (сегменты без границ), `PATCH` вообще возвращал пустую полосу | единый `MonitorMapper.ToBar` с границами сегментов; `PATCH` отдаёт такую же полосу, как дашборд | `ListMonitorsTests`, `CreateMonitorTests` (24 сегмента, unknown без данных) |
| 8 | «Последняя задержка» бралась как `history[0]`, хотя порядок в контракте порта не зафиксирован | `MaxBy(c => c.CheckedAt)` в `GetMonitor` и `ListMonitors` | `ListMonitorsTests` (last latency 11 при неотсортированном входе) |
| 9 | Пробер, бросивший исключение, ронял всю итерацию | `RunDueChecks`: проба в try/catch (кроме отмены), результат — неуспешная проверка с причиной во внутреннем логе; `checkedAt` берётся после пробы, чтобы интервал считался от завершения | `RunDueChecksTests` (79/79) |
| 10 | `CheckResult.FromProbe` принимал отрицательную задержку | домен-гард: задержка < 0 → `DomainValidationException` | `CheckResultTests` |
| 11 | `User.Register` пропускал `a@b`, `a@@b`, `..` в домене | нормализация email ужесточена: ровно один `@`, непустые части, нет пробелов и `..`, домен с точкой | `UserTests` (валидные и невалидные кейсы) |
| 12 | Ключи ошибок валидации расходились: `Email`/`Password` против `url`/`intervalSeconds` | `AuthValidationFields` — camelCase (`email`, `password`), как и у мониторов | валидаторы + `ExceptionStatusMapperTests` |
| 13 | `IProbeService` не фиксировал контракт «не бросает исключений» | контракт описан в XML-doc порта (единственное исключение — отмена) | — |
| 14 | Rate limit: одна политика на регистрацию и логин, регистрация дороже | две политики (`RateLimit__LoginPermitLimit=20`, `RegisterPermitLimit=10`), старый `AuthPermitLimit` — фолбэк | `smoke-api.ps1` (живой 429 с `Retry-After`) |
| 15 | За nginx лимит общий, `X-Forwarded-For` игнорировался молча | `AddProxyForwardedHeaders`: выключено по умолчанию, включается явным списком прокси/сетей (`ForwardedHeaders__*`), `ForwardLimit = 1` | `ForwardedHeadersSetup` + описание границы в §6.5 |
| 16 | `/openapi/v1.json` и `/scalar/v1` были доступны в любом окружении | оба маппинга — только в Development | `smoke-api.ps1` (идёт в Development) |
| 17 | Сид демо-учётки работал вне Development, хотя пароль известен из исходников | старт с `SeedOnStart` вне Development запрещён с объяснением в тексте ошибки | — |
| 18 | `Jwt__Secret` не проверялся на длину на старте, секрет-заглушка уезжала в прод | `JwtOptions` биндится и валидируется в Infrastructure (`JwtSecretValidator`, ≥ 32 байта), Api добавляет `ValidateOnStart` | `JwtBearerSetupTests`, `JwtTokenRoundTripTests` |
| 19 | Воркер не проверял конфигурацию на старте: `Worker__TickSeconds=0` давал busy-loop | `IStartupValidator.Validate()` в `WorkerHost` + `Logging:LogLevel` применяется в самодельном хосте | запуск с `Worker__TickSeconds=0` → `OptionsValidationException` (проверено вручную) |
| 20 | JSON-логи воркера были в другом формате, чем у Api (разные ключи), `ScopeStack` терял вложенность | ключи как у `AddJsonConsole`, scopes через `AsyncLocal`-цепочку, формат времени с экранированием | — |
| 21 | `IsEnabled(None)` возвращал `true`; битый JSON-файл конфигурации падал с NRE | `IsEnabled` → `logLevel != None`; парсер оборачивает ошибку в `InvalidOperationException` с именем файла; скалярный корень больше не роняет `Flatten` | — |
| 22 | `UpdateMonitor` возвращал `UptimeBar = []` (фронт рисовал другую разметку) | полоса строится тем же `MonitorMapper`, что и на дашборде | `ListMonitorsTests` |
| 23 | Заглушка `migrate` в prod-стенде поднимала второй Api | `--migrate` в `Pingboard.Api` — применить схему и выйти (веб-сервер не поднимается) | запуск с недоступной БД: `--migrate` идёт в Postgres и падает, Kestrel не стартует |
| 24 | `docker compose` читал `.env` из `deploy/`: `POSTGRES_*` подставлялись пустыми | умолчания `${POSTGRES_*:-…}` + `--env-file .env` из корня репозитория в документации и комментариях | `docker compose config` (см. §3.2) |
| 25 | Порт 8080 конфликтовал: web и api публиковались на один хост-порт; без фронта стенд не поднимался вовсе | Api — `${API_BIND:-0.0.0.0:8080}`, web — профиль `web` и `${WEB_BIND:-8081}`; точка настройки портов одна (в prod-файле порт не переопределяется, compose складывает списки `ports`) | — |
| 26 | У воркера не было healthcheck: зависший цикл выглядел здоровым | файл-пульс (`Worker__HeartbeatPath`) + healthcheck compose на свежесть файла | запуск воркера: файл пишется даже когда все итерации падают (БД недоступна) |
| 27 | `SeedDefaultUserAsync`: занятый email → ошибка UNIQUE без объяснения | поиск по `Id` или email + явное исключение с подсказкой | — |

Третий проход — уже не по вехам, а сквозь призму 12 факторов: что заявлено о каждом факторе и что
реально в коде, с открытыми пунктами и их приоритетами. Результат — [REVIEW-12FACTORS.md](REVIEW-12FACTORS.md).
