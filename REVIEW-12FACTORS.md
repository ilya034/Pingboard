# Pingboard — ревью по 12 факторам

Ревью проверяет ровно одно: **соответствуют ли заявления о факторах тому, что реально лежит в коде,
конфигурации и деплой-файлах**. База для сверки — таблица факторов в [PLAN.md](PLAN.md) §10, разделы
[README.md](README.md) §5–§6.5 и [DEPLOY.md](DEPLOY.md). Каждое расхождение либо закрыто правкой
формулировки (§3), либо вынесено открытым пунктом с предложением (§4.2); три пункта приоритета P1
были закрыты сразу, уже в коде и конфиге (§4.1).

Формат — как у ревью M0/M1 в README §10–§11: «замечание → чем закрыто → чем проверено».

---

## 1. Как проводилось

Проверено чтением кода, конфигов и состояния git, а также прогоном сборки и тестов (§5):

* `git ls-files`, `git check-ignore`, `git diff` — что реально в репозитории (фактор I, III);
* `backend/Directory.Packages.props`, `Directory.Build.props`, `NuGet.config`, `frontend/package*.json`
  — объявление и запиннинг зависимостей (фактор II);
* `.env`, `.env.example`, `.dockerignore`, `appsettings*.json` обоих процессов, `DependencyInjection.cs`
  — источники конфигурации и fail-fast (факторы III, IV);
* `deploy/Dockerfile.api`, `deploy/Dockerfile.web`, `deploy/docker-compose.yml`,
  `deploy/docker-compose.prod.yml`, `deploy/nginx.conf` — сборка, релиз, порты, паритет (факторы V, VII, X);
* `Program.cs` Api и Worker, `WorkerHost.cs`, `DueCheckWorker.cs`, `HealthEndpoints.cs`,
  `AuthRateLimiting.cs`, `JwtSecretProvisioning.cs`, `MonitorRepository.cs` — процессы, disposability,
  логи, админ-режимы;

**Не проверено** (нет живого стенда: Docker-демон и Postgres на этой машине недоступны, см. README §7
и [SANDBOX.md](SANDBOX.md)): сборка образов, `docker compose up`, команды `--scale`, применение миграций
к живой БД, балансировка nginx между репликами, реальный SIGTERM/SIGKILL-сценарий, поведение
`ForwardedHeaders` за настоящим прокси. Всё, что ниже помечено как подтверждённое, подтверждено
статически (код/конфиг/тесты), а не экспериментом на стенде.

**Правка от 02.10.2026 (после ревью).** Два пункта P2 из §4.2 закрыты — lock-файлы NuGet и
пробелы в `.env.example`; подробности в §8. Итоговый прогон сборки и тестов после этих правок:
0 предупреждений, 79/79 тестов. Таблица §2 описывает состояние **на момент ревью**, поэтому
её вердикт по фактору II («Lock-файлов NuGet нет») устарел: там теперь стоит §8.

---

## 2. Сводка по факторам

Вердикты: ✅ заявление соответствует коду; ⚠️ соответствует с оговоркой (оговорка в последней
колонке); ❗ заявление расходилось с кодом — закрыто правкой формулировки (§3) или открытым пунктом (§4).

Номера строк в ссылках — на момент проверки; во время ревью дерево правилось параллельно (см. §6),
поэтому к ним стоит относиться как к ориентиру, а не как к точному якорю.

| # | Фактор | Заявление | Что в коде и конфиге | Вердикт |
|---|---|---|---|---|
| I | Codebase | один репозиторий (бек + фронт + deploy); dev и «прод» — два deploy'я одной базы | Один `.git`; в `git ls-files` нет `bin/`, `obj/`, логов; `.env` игнорируется (`.gitignore:16`, проверено `git check-ignore`); дев и «прод» — `deploy/docker-compose.yml` + `deploy/docker-compose.prod.yml` | ✅ |
| II | Dependencies | версии централизованы и запинены; в рантайм-образе нет SDK | `backend/Directory.Packages.props` — 23 `<PackageVersion>`, 35 `<PackageReference>` без версий, `CentralPackageTransitivePinningEnabled=true`; рантайм — `aspnet:10.0` без SDK (`Dockerfile.api:33`); фронт — `package-lock.json` в git + `npm ci` (`Dockerfile.web:9`). **Lock-файлов NuGet нет** (`RestorePackagesWithLockFile` не задан), README §1 называл неверное число пакетов (17 вместо 23) | ❗ → §3 п.1–2; lock-файлы остаются открытым пунктом — §4.2 п.1 |
| III | Config | все настройки из env; `.env` не в git, в репо `.env.example`; секретов нет в appsettings | `.env` вне индекса и вне контекста сборки; `appsettings.json` обоих процессов без секретов; отсутствие `ConnectionStrings__Default` роняет старт с понятным текстом (`DependencyInjection.cs:26-28`); env — последний источник у Api и у воркера (`WorkerHost.cs:69-74`) | ⚠️ dev-умолчания дублируются в `appsettings.json`; `.env.example` не полный (§3 п.6, §4.2 п.2) |
| IV | Backing services | Postgres «прикреплён» env-строкой; смена инстанса — смена переменной | Единственный источник строки — `ConnectionStrings__Default`; захардкоженных хостов/портов БД в коде нет (grep по `Host=postgres`, `localhost:5432` даёт только `.env` и доки); readiness ходит в БД через порт `IDatabaseHealthProbe`, а не через EF Core в Api | ✅ |
| V | Build, release, run | build = multi-stage; release = тег образа + миграции; run = compose | multi-stage есть (`Dockerfile.api` — build/runtime); миграции — админ-режим `dotnet Pingboard.Api.dll --migrate` той же сборки (`Program.cs:13-24`) + профиль `migrate`; **тега образа нет**: оба compose-файла содержат только `build:`, образ пересобирается `--build` на целевой машине, `Version=0.1.0` (`Directory.Build.props:21`) не попадает ни в тег, ни в label; migrate-bundle — M5. Тег `IMAGE_TAG` и OCI-label добавлены уже после ревью (§4.1 п.1) | ⚠️ расхождение закрыто: образ получил тег и OCI-label (§4.1 п.1); открытым остаётся продвижение артефакта из реестра — §4.2 п.3 |
| VI | Processes | stateless: JWT вместо сессий, расписание воркера — в БД | Серверных сессий нет; `IHostedService` в Api нет (grep); расписание — SQL-запрос. Процесса-локальное состояние ограничено тремя вещами: счётчики rate limit (`AuthRateLimiting.cs:84-94`), эфемерный dev-ключ (`JwtSecretProvisioning.cs:41`), флаг «о degraded уже сообщили» (`HealthEndpoints.cs:15,64` — на ответы не влияет) | ⚠️ → §3 п.4 |
| VII | Port binding | Kestrel слушает `ASPNETCORE_URLS=http://+:8080`, приложение самодостаточно | `ENV ASPNETCORE_URLS=http://+:8080` в образе (`Dockerfile.api:47`) и в compose; `UseHttpsRedirection` в коде нет (за TLS-терминатором это правильно); у воркера порта нет вовсе | ✅ |
| VIII | Concurrency | два process type из одного образа; реплики масштабируются | Один образ, две команды (`api`, `worker`); реплики воркера безопасны — CAS-захват в `MonitorRepository.ListDueAsync` (условный `UPDATE` + штамп `now`); репликам Api мешают фиксированный `ports: ${API_BIND}:8080` (`docker-compose.yml:48-51`, снимается надстройкой `docker-compose.scale.yml` — §4.1 п.2) и эфемерный ключ подписи при пустом `Jwt__Secret`; nginx резолвит `api:8080` один раз при старте (`nginx.conf:22`) | ⚠️ формулировка была неверна («только один воркер») — сведена с кодом (§3 п.9); `--scale api=2` теперь рабочий (§4.1 п.2), остаются лимиты по репликам и захват по штампу (§4.2 п.4–5) |
| IX | Disposability | SIGTERM → graceful shutdown (воркер доигрывает), `ShutdownTimeout=10s`, быстрые healthcheck'и | Api: `HostOptions.ShutdownTimeout=10s` (`Program.cs:75`); воркер: `PosixSignalRegistration` для SIGTERM + бюджет 10 с (`WorkerHost.cs:30,134,187-194`); healthcheck'и: api — 10 с/3 с, воркер — свежесть пульса `< 120 c` (`docker-compose.yml:55-79`) | ✅ расхождение закрыто: `stop_grace_period: 15s` у `api` и `worker` (§4.1 п.3) |
| X | Dev/prod parity | локально и на VPS — те же образы, отличия только в env | Один и тот же Dockerfile и код для обоих стендов; отличия сведены к env: `ASPNETCORE_ENVIRONMENT: Production`, `MigrateOnStart: "false"` (`docker-compose.prod.yml:16-24`), `PGDATA_PATH`, `API_BIND` | ❗ «те же образы» → «тот же Dockerfile, образ собирается на месте» (§3 п.5) |
| XI | Logs | только stdout/stderr, JSON, никаких файлов | Api: `ClearProviders()` + `AddJsonConsole` с UTC-таймстампом (`Program.cs:33-41`); воркер: свой провайдер в `Console.Out` (`JsonConsoleLogger.cs:61`), уровни из конфигурации (`WorkerHost.cs:167-180`); файловых синков нет — единственная запись в файл во всём решении это пульс воркера в `/tmp` (`DueCheckWorker.cs:79`) | ✅ |
| XII | Admin processes | `migrations add`/`bundle`, сид, `pingboard-migrate` — one-off процессы | `--migrate` — отдельный режим той же сборки, веб-сервер не поднимается, профиль `migrate` с `restart: "no"`; сид `SeedDefaultUserAsync` запрещён вне Development (`Program.cs:112-115`); `dotnet ef migrations add` документирован (README §8); bundle — M5 | ✅ |

---

## 3. Что исправлено сразу (только формулировки)

| # | Фактор | Было | Стало | Где |
|---|---|---|---|---|
| 1 | II | «`Directory.Packages.props` (центральные версии), `packages.lock.json`» | центральные версии + `CentralPackageTransitivePinningEnabled`; у фронта lock-файл есть, у NuGet — нет (открытый пункт) | PLAN.md §10 |
| 2 | II | README §1: «✅ 17 пакетов» | «✅ 23 центральные версии» (посчитано по `Directory.Packages.props`) | README §1 |
| 3 | V | «release = тег образа + прогон миграций (bundle)» | релиз = сборка того же Dockerfile на целевой машине + явный прогон миграций (`--migrate`); тег/immutable-артефакт — CI (расширение №4), bundle — M5 | PLAN.md §10 |
| 4 | VI | «stateless: JWT, расписание в БД» (без оговорок) | добавлено: процесса-локальное состояние ограничено счётчиками лимитов, эфемерным dev-ключом и флагом дедупликации логов readiness | PLAN.md §10 |
| 5 | X | «локально крутятся те же образы, что на VPS» | «тот же Dockerfile и код, образ собирается на месте; отличия сведены к env (перечислены)» | PLAN.md §10 |
| 6 | III | README §5: «Полный список — в `.env.example`» | в `.env.example` рабочий набор; отсутствующие там ключи с безопасными дефолтами (`SeedOnStart`, `Probe__HealthyStatusCodes`, `Probe__UseHeadWithGetFallback`, `Logging__LogLevel__*`) названы явно и указаны на DEPLOY §3 / `appsettings.json` | README §5 |
| 7 | XII | «`dotnet ef migrations add` / `bundle`, сид-скрипт, `pingboard-migrate`» | `migrations add`, сид с запретом вне Development, `dotnet Pingboard.Api.dll --migrate`; bundle — M5 | PLAN.md §10 |
| 8 | — | README §11: «`web/` пуст» (папки `web/` в репозитории нет) | указано, что текущее имя SPA — `frontend/` | README §11 |
| 9 | VIII | формулировки о масштабировании (воркер «только один», `--scale api=2` «работает») | дублей не даёт CAS-захват; репликам Api мешают порт и ключ подписи | PLAN §6, §10, §12, §14; DEPLOY §1, §5.7; README §3.2, §6.1.1, §6.5 |

---

## 4. Открытые пункты и что уже закрыто

### 4.1 Закрыто сразу после ревью (три пункта P1)

| # | Фактор | Что было | Что сделано | Чем проверено |
|---|---|---|---|---|
| 1 | V | у образа не было ни имени, ни тега: `build:` без `image:`, `Version=0.1.0` никуда не попадала | `image: pingboard-api:${IMAGE_TAG:-local}` у `api`, `worker` и `migrate` (один артефакт на релиз — фактор VIII остаётся в силе: три команды, один образ), `build.args.APP_VERSION` → OCI-label в `deploy/Dockerfile.api`; `IMAGE_TAG`/`APP_VERSION` описаны в `.env.example` и DEPLOY §3; §5.5 переписан на «собрали под тегом → откат переключением тега» | `docker compose config` для всех трёх наборов файлов (базовый, scale, prod+migrate): у `api`/`worker`/`migrate` `image: pingboard-api:local` и `APP_VERSION: 0.1.0` |
| 2 | VIII | `--scale api=2` падал бы на `port is already allocated`: в базовом compose у `api` фиксированный `ports: ${API_BIND}:8080` | новая надстройка `deploy/docker-compose.scale.yml` с `ports: !reset []` (compose ≥ 2.24) + рабочие команды в DEPLOY §5.7 и PLAN §14; в комментариях базового compose указано, почему порт и реплики несовместимы, и что демонстрация идёт через надстройку | `docker compose config` с надстройкой: у `api` больше нет ни одной строки `published`, exit code 0 |
| 3 | IX | `stop_grace_period` не задан нигде: дефолт Docker (10 с) совпадал с бюджетом приложения | `stop_grace_period: 15s` у `api` и `worker`; правило описано в DEPLOY §5.7 (строка «Остановка контейнеров») и в факторе IX PLAN §10 | `docker compose config`: `stop_grace_period: 15s` у обоих сервисов |

В `deploy/nginx.conf` при этом **осознанно ничего не менялось**. Вариант «динамическое разрешение
реплик» (`resolver 127.0.0.11` + `proxy_pass` через переменную) убрал бы шаг `nginx -s reload`, но
проверить его на этой машине нечем, а ошибка в `proxy_pass` ломает сразу `/api`, `/healthz` и `/readyz`.
Оставлено проверяемое решение: статический `proxy_pass http://api:8080` плюс явный `nginx -s reload`
после добавления реплики.

### 4.2 Остаётся открытым

| # | Фактор | Проблема | Почему важно | Предложение | Приоритет |
|---|---|---|---|---|---|
| 1 | II | Lock-файлов NuGet нет: `RestorePackagesWithLockFile` не задан, `packages.lock.json` отсутствует. Воспроизводимость сборки держится на центральных версиях, но транзитивные зависимости могут «поехать» при обновлении фида | «запинено» в факторе II сейчас верно лишь наполовину; у npm это уже сделано, у NuGet — нет | `<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>` в `Directory.Build.props`, закоммитить `packages.lock.json` по проектам, в CI — `dotnet restore --locked-mode` | P2 |
| 2 | III | `.env.example` не содержит `SeedOnStart`, `Probe__HealthyStatusCodes`, `Probe__UseHeadWithGetFallback`, `Logging__LogLevel__*` (первый разобран только в DEPLOY §3, остальные — дефолты в коде) | шаблон — то, с чего начинают развёртывание; неполный шаблон = сюрпризы на «проде» | добавить закомментированные строки с дефолтами и пометкой «менять осознанно» | P2 |
| 3 | V | продвижения артефакта нет: образ собирается на целевой машине и остаётся локальным (`IMAGE_TAG` это фиксирует, но не переносит) | «собрали на проде» и «закатили проверенный артефакт» — разные вещи; без реестра нет гарантии, что на стенде именно тот билд | CI: сборка в реестр (`docker buildx build --push`) + на хосте только `pull` по неизменяемому тегу (расширение №4) | P2 |
| 4 | VIII | Rate limit живёт в памяти процесса, а за nginx ключ раздела — адрес прокси | при репликах лимит умножается на их число, а за прокси становится общим на стенд | уже задокументировано как граница (README §6.5); закрывается распределённым счётчиком — M5 | P3 |
| 5 | VIII | Захват мониторов опознаётся по равенству `LastCheckedAt == now` (`MonitorRepository.cs:63-65`), то есть корректность опирается на уникальность штампа времени у двух процессов, а не на идентичность захвата | при совпадении штампа (практически невозможно, но теоретически) вторая реплика получила бы чужие строки; плюс лишний round-trip на итерацию | `FOR UPDATE SKIP LOCKED` + `RETURNING` (расширение №6) — снимает и второй запрос, и «пропущенный интервал» при падении после захвата | P3 |
| 6 | X | Контейнер `web` работает от root и без healthcheck (в `api` — `USER $APP_UID`) | nginx от root — унаследованный дефолт образа; отсутствие healthcheck означает, что «web поднят» проверяется только фактом запуска процесса | `USER nginx` + healthcheck `wget -qO- localhost/` (или `nginx -t`-подобная проверка) | P3 |

---

## 5. Прогон сборки и тестов (проверка утверждений README §1)

```
dotnet build backend/Pingboard.sln -m:1   →  Сборка успешно завершена. Предупреждений: 0, Ошибок: 0
in-process раннер (backend/scripts/TestRunner):
  Pingboard.Domain.Tests        passed=32 failed=0
  Pingboard.Application.Tests   passed=47 failed=0
  ВСЕ ТЕСТЫ ПРОШЛИ
```

Прогонов было два: первый — на дереве в начале ревью, второй — после того, как параллельный процесс
(§6) прошёл по `AuthEndpoints.cs`, `HealthEndpoints.cs`, `MonitorEndpoints.cs` и `Program.cs`. Оба дали
одно и то же: 0 предупреждений при `TreatWarningsAsErrors=true` и 79/79 тестов. То есть утверждения
README §1 («собирается, 0 предупреждений», «79 тестов: 32 Domain + 47 Application») на текущем дереве
верны, и правки в этих четырёх файлах ничего не сломали.

Заодно подтверждён описанный в SANDBOX.md фолбэк: штатный `vstest`-хост падает на `OpenProcess`
(песочница), скрипт переключается на in-process раннер — README §7 описывает реальное поведение.

---

## 6. Замечания вне 12 факторов

* **Валидация `JwtOptions` на старте есть ровно там, где она нужна** — это проверено отдельно, потому
  что выглядит как пропуск: валидаторы секции живут в Infrastructure (`DependencyInjection.cs:66-71`)
  без `ValidateOnStart()`, а fail-fast добавляет Api отдельным вызовом (`AuthenticationSetup.cs:19`).
  Воркер `JwtOptions` не резолвит вовсе, поэтому его старт от `Jwt__Secret` не зависит — свойство,
  а не дыра (README §11 п.18 описывает это верно).
* **Параллельная правка исходников во время ревью.** Пока шло ревью (21:21–21:31), рабочее дерево
  менял посторонний процесс — не этот проход и не в рамках какой-либо задачи ревью. Характер правок:
  удаление пояснительных комментариев и перевод русских текстов на английский. По времени: сначала
  `Domain/Entities` (`CheckResult.cs`, `Monitor.cs`, `User.cs`, `Common/Entity.cs`, 21:21–21:24 — эти
  правки **откатились**, `git status` их больше не показывает), затем `Api`: `Endpoints/AuthEndpoints.cs`
  (21:25), `Endpoints/HealthEndpoints.cs` (21:28), `Endpoints/MonitorEndpoints.cs` (21:29),
  `Program.cs` (21:30). Содержательно правки местами ухудшают текст: `"Readiness: SELECT 1 до Postgres"`
  → `"Readiness: SELECT 1 Postgres"` (потерян смысл), `Liveness и readiness — это разные вопросы`
  → `Liveness и readiness(§10 PLAN.md)`, у `LogTransition` удалён doc-комментарий, объясняющий, почему
  логируется только смена состояния. Компиляция и тесты при этом зелёные (см. §5), но:
  * это прямо конфликтует со стилем репозитория — подробные русские doc-комментарии, на которые
    ссылаются README §10–§11 и PLAN §13 («грабли чистой архитектуры»);
  * `[PLAN.md](PLAN.md)` §10/§14 и README §6 ссылаются на комментарии как на источник объяснений —
    если чистка продолжится, ссылки станут указывать в пустоту;
  * номера строк в таблицах §2 уже «поехали» из-за этих удалений.
  Решение (продолжать/остановить/откатить) — за владельцем репозитория: правки не мои, сам я их
  не трогал и не откатывал.
* **DEPLOY.md §5.7** теперь честно помечает команды масштабирования как непроверенные на этой машине —
  статус заявлений по факторам VIII/IX стоит поднять, как только появится живой стенд.
* **Тот же рассинхрон языков дал падающий тест.** Прогон 02.10.2026 до правок: 78 passed, 1 failed —
  `ExceptionStatusMapperTests.Racing_registrations_on_one_email_are_400_on_the_email_field` ждал русский
  текст, а `ExceptionStatusMapper` к тому моменту отдавал английский. Владелец репозитория тест
  поправил; после этого снова 79/79 (см. §8). Это ровно тот класс поломки, о котором предупреждает
  пункт выше: правка формулировок не ломает сборку, но ломает утверждения и тесты.

---

## 8. Правка 02.10.2026: закрыты пункты P2 (§4.2 №1 и №2)

Отдельный проход по двум открытым пунктам средней важности. Вердикты §2 оставлены как исторические
(на момент ревью), актуальное состояние — здесь.

| # | Фактор | Что было | Что сделано | Чем проверено |
|---|---|---|---|---|
| 1 | II | `RestorePackagesWithLockFile` не задан, `packages.lock.json` отсутствовал: центральные версии пинили только прямые зависимости, транзитивные могли «поехать» при обновлении фида | `<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>` в `Directory.Build.props`; сгенерированы и закоммичены 7 lock-файлов (5 приложений + 2 тестовых проекта) с `resolved` + `contentHash` каждого пакета, включая транзитивные и `CentralTransitive` (`Microsoft.EntityFrameworkCore.*`, `Npgsql`, `Microsoft.IdentityModel.*`, `Microsoft.OpenApi`); restore в образе — `--locked-mode`, а `deploy/Dockerfile.api` копирует lock-файлы в build-стейдж до `dotnet restore` | `dotnet restore backend/Pingboard.sln --locked-mode` → exit 0; имитация дрейфа (добавил `PackageReference` в `Pingboard.Application.csproj`) → `error NU1004` с перечислением расхождений, exit 1; после отката — снова exit 0; сборка решения и 79/79 тестов |
| 2 | III | `.env.example` не содержал `SeedOnStart`, `Probe__HealthyStatusCodes`, `Probe__UseHeadWithGetFallback`, `Logging__LogLevel__*` — то есть шаблон, с которого начинают развёртывание, знал не про все ручки | все четыре группы добавлены закомментированными строками с дефолтами и пометкой «менять осознанно»; у массивов описано правило «индексы подряд от нуля, пустых значений быть не должно» (пустая строка в `Probe__HealthyStatusCodes__0` роняет старт при биндинге в `int[]`); в группе `ForwardedHeaders` та же оговорка — пустая строка не парсится ни в `IPAddress`, ни в сеть | поиск по файлу: все четыре имени присутствуют; значения совпадают с дефолтами в `appsettings.json` и в классах Options (`MonitorsOptions`, `ProbeOptions`, `WorkerOptions`, `AuthRateLimiting`) |

Заодно синхронизированы формулировки в трёх местах, чтобы они не расходились с кодом:
[PLAN.md](PLAN.md) §10 фактор II (вместо «Lock-файлов NuGet нет» — описание lock-файлов и
`--locked-mode`), README §1 (отдельная строка про `packages.lock.json`) и README §5 (список
ключей, которых «нет в `.env.example`», заменён на «есть, закомментированы»); DEPLOY §3 получил
процедуру обновления lock-файла при смене зависимостей и объяснение ошибки `NU1004`.

Остаются открытыми пункты §4.2 №3–6 (продвижение артефакта из реестра, распределённый rate limit,
`FOR UPDATE SKIP LOCKED`, root/healthcheck у `web`) и пункты 5–9 из свежего прохода (design-time
строка в `UptimeDbContextFactory`, ссылка фронта на `localhost:8080/scalar/v1`).
