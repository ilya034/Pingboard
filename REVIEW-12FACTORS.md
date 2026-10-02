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

Остаются открытыми пункты §4.2 №3–5 (продвижение артефакта из реестра закрыто в §9, распределённый
rate limit, `FOR UPDATE SKIP LOCKED`) и пункты 5–9 из свежего прохода (design-time строка
в `UptimeDbContextFactory`, ссылка фронта на `localhost:8080/scalar/v1`). Пункт §4.2 №6
(root/healthcheck у `web`) закрыт в §10.

---

## 9. Деплой в новых условиях: реестр, VPS 1–2 ГБ, nginx хоста (CI/CD)

Проектирование конвейера доставки (расширения №4 и №9 из PLAN.md §12) — и заодно проверка,
что факторы в новых условиях не «переезжают» молча.

**Что заведено в репозитории**

| Файл | Роль | Проверено |
|---|---|---|
| `.github/workflows/ci.yml` | `test` (сборка + 79 тестов на ubuntu) → `build-push` (единый образ api+worker в GHCR под тегом `sha-<commit>`) → `deploy` (ручной `workflow_dispatch`, выкат по SSH) | YAML разобран парсером: три job'а, у `build-push` права `packages: write`, у `deploy` условие `workflow_dispatch` |
| `deploy/docker-compose.registry.yml` | заменяет `build:` на `image:` — на VPS только `pull`; без `IMAGE`/`IMAGE_TAG` падает сразу | `docker compose config` → образы `ghcr.io/owner/…:sha-abc1234`, секции `build` не остаётся; запуск без переменных → `required variable IMAGE is missing a value`, exit 1 |
| `deploy/docker-compose.vps.yml` | лимиты памяти (512/384/64 МБ), GC-лимит .NET, `Worker__MaxParallel=4`, ротация `json-file` 10 МБ × 3 | в resolve-конфиге `mem_limit`, `logging.options`, `DOTNET_GCHeapHardLimitPercent` у нужных сервисов; порты публикуются на `127.0.0.1` |
| `deploy/nginx-host.dev.conf` | внешний nginx: TLS-терминация + прокси на `127.0.0.1:8081` | конфиг вычитан; на живом nginx не прогонялся |
| `scripts/k3d-up.ps1` | локальный k3s-стенд: кластер с ротацией логов kubelet, `imagePullSecret` для GHCR, `kubectl apply -k` | синтаксис PowerShell разобран (UTF-8 BOM обязателен для 5.1); на живом кластере не запускался |

**Что меняется по факторам**

* **V (build/release/run)** — из ⚠️ в ✅ по факту появления конвейера: тег `sha-<commit>` неизменяем,
  продвижение артефакта есть, откат — переключением тега. На VPS не собирается ничего: сборка
  перенесена в runner, где есть и ресурсы, и сеть (на 1–2 ГБ хосте сборка SDK+node рисковала
  OOM-killer'ом).
* **III (config)** — `.env` на VPS генерируется workflow'ом из секретов под `umask 077`, в git его нет;
  `ForwardedHeaders__Enabled=true` включается вместе с `KnownNetworks=172.16.0.0/12`, иначе доверие
  заголовку либо открыто всем (подделка `X-Forwarded-For`), либо выключено (общий лимит на логин
  для всего стенда).
* **IX (disposability)** — на VPS действует тот же `stop_grace_period: 15s` (образ и приложение не
  менялись), плюс проверка `/readyz` после выката: «поднялось» проверяется доступностью БД, а не
  фактом запуска контейнера.
* **XI (logs)** — добавлена ротация на уровне docker (`json-file`, 10 МБ × 3): на dev-стенде Api
  пишет лог каждые 5 секунд, без лимита диск VPS кончается заметно раньше, чем хотелось бы.
* **X (dev/prod parity)** — появился третий способ запуска того же образа (compose-на-хосте,
  compose-из-реестра, k8s), поэтому в DEPLOY §5.9 явно перечислено, чем они отличаются: только env,
  лимиты и способ попадания образа на машину.

**Остаётся открытым после этого шага** (перенесено в план работ): распределённый rate limit,
`FOR UPDATE SKIP LOCKED`, design-time строка в `UptimeDbContextFactory`, ссылка фронта
на `localhost:8080/scalar/v1`. Манифесты k8s и пункт про root у `web` — в §10.

---

## 10. k8s-стенд: манифесты и что это меняет по факторам

Манифесты заведены (расширение №9), проверены рендером и инвариантами — без живого кластера.

**Состав:** `k8s/base` (Namespace, ConfigMap, StatefulSet postgres с PVC, Deployment api/worker/web,
Service'ы, Ingress, NetworkPolicy, Job migrate, образец Secret'а), `k8s/overlays/dev` и
`k8s/overlays/prod`, `scripts/k3d-up.ps1`, `k8s/dev.env.example`.

**Чем проверено** (Docker-демон из песочницы недоступен, кластера нет):

* `kubectl kustomize k8s/overlays/dev` и `.../prod` — рендер без ошибок (kustomize v5.8.1), 12 документов
  в наборе (Job migrate намеренно вне набора: одноразовый, применяется отдельной командой);
* по рендеру прогнаны смысловые инварианты (скрипт на Node, разбор без YAML-парсера): селекторы
  Service совпадают с метками подов одноимённого workload, пробы ссылаются на объявленные порты,
  у всех Deployment есть `requests` и `terminationGracePeriodSeconds`, `volumeMounts` ссылаются на
  объявленные `volumes`, Ingress указывает на существующий Service и его порт, Secret'а в наборе нет
  (секреты не в git), заполнитель `OWNER` в образах отдаётся предупреждением;
* патчи оверлея dev проверены по рендеру: `ASPNETCORE_ENVIRONMENT=Development`, `MigrateOnStart=true`,
  `SeedOnStart=true`, доверие `X-Forwarded-For` от pod-сети.

**Закрытые пункты:** §4.2 №6 (контейнер `web` от root и без healthcheck) — новый
`deploy/nginx.container.conf` слушает 8080 под `USER nginx`, `HEALTHCHECK` в Dockerfile.web,
а в k8s это закреплено `runAsNonRoot: true` + `readOnlyRootFilesystem`. Пункт §4.2 №3 (продвижение
артефакта) закрыт ещё в §9 конвейером.

**Что меняется по факторам:**

* **VIII (concurrency)** — исчезают обе инфраструктурные границы, которые были у compose:
  фиксированный хост-порт и однократный резолв имени nginx'ом. Реплики Api — это `replicas: N`,
  наружу порт не публикуется вовсе. Остаётся только лимит rate limit в памяти процесса.
* **IX (disposability)** — `terminationGracePeriodSeconds: 30` вместо `stop_grace_period: 15s`:
  бюджет приложения тот же (10 с), но кластеру нужно время снять endpoint до остановки процесса.
* **XI (logs)** — ротация переезжает с докер-демона на kubelet (`container-log-max-size=10Mi`,
  `container-log-max-files=3`), задаётся при создании кластера в `scripts/k3d-up.ps1`.
* **XII (admin processes)** — миграции стали Job'ом из того же образа, приложение с
  `MigrateOnStart=false`: админ-процесс перестал быть «режимом приложения» даже на dev-стенде.
* **IV (backing services)** — то, что в compose давала сеть Docker, здесь выражено явно:
  NetworkPolicy пускает к Postgres только `api`/`worker`/`migrate`, а к Api — только `web`.
* **VI (processes)** — `/tmp` воркера стал `emptyDir` пода: пульс живёт ровно столько, сколько
  живёт под, и это ровно то поведение, которое обещано в §2.

**Новая открытая граница (появилась вместе с k8s):** проба воркера — `exec` с `date`/`stat`,
то есть зависит от GNU coreutils и `sh` в образе. Для distroless/chiseled-образа это сломается;
правильное решение — режим `--self-check` в самом воркере (бинарник читает пульс и возвращает код).
**Закрыто в §11.**

---

## 11. Проба живости воркера: режим `--self-check` (закрытие границы из §10)

Правка кода, а не манифестов: `dotnet worker/Pingboard.Worker.dll --self-check` проверяет свежесть
пульса внутри самого процесса и возвращает 0/1. Границу из §10 это снимает полностью — проба больше
не зависит от `date`/`stat` и `sh` в образе, то есть переживёт переход на distroless/chiseled-базу.

**Что сделано**

* [WorkerSelfCheck.cs](backend/src/Pingboard.Worker/Hosting/WorkerSelfCheck.cs) — разбор аргументов
  (`--self-check`, `--heartbeat <путь>`, `--max-age <секунды>`), конфигурация через те же
  JSON-файлы и переменные окружения, что у обычного старта, и проверка свежести;
* возраст считается **по отметке внутри файла**, а не по времени изменения: проверка не зависит
  от точности ФС и одинаково работает на смонтированном томе и на Windows;
* `Worker__HeartbeatMaxAgeSeconds` (по умолчанию 120 с) вынесен в `.env.example`, ConfigMap k8s
  и в таблицу DEPLOY §3; кривое значение (0, отрицательное, не число) откатывается к умолчанию,
  а не «нездоров всегда»;
* проба в [docker-compose.yml](deploy/docker-compose.yml) и `livenessProbe` в
  [k8s/base/40-worker.yaml](k8s/base/40-worker.yaml) теперь одна и та же команда — раньше это были
  две разные реализации одной проверки (shell в compose, shell в k8s), и они могли разойтись;
* `Environment.Exit` вместо `return` в точке входа: top-level statements требуют, чтобы значение
  возвращали все пути, а обычный запуск воркера ничего не возвращает (цикл живёт до SIGTERM).

**Чем проверено**

* 11 новых тестов (`Worker/WorkerSelfCheckTests.cs`): режим не включается без флага; свежий пульс → 0;
  устаревший → 1; **ровно на границе порога → 0** (ловит сдвиг `<=` на `<`); файла нет → нездоров
  с объяснением; пустой путь → нездоров; битый файл → нездоров, а не исключение; возраст берётся
  из содержимого (файл с свежим mtime, но старой отметкой → нездоров);
  порог и путь читаются из конфигурации; аргумент перебивает конфигурацию;
* прогон 90/90 (32 Domain + 58 Application), сборка 0 предупреждений;
* **живой прогон собранного бинарника** (не только тесты): свежий пульс → exit 0, устаревший
  (`--max-age 1` через 2 с) → exit 1, отсутствующий файл → exit 1, путь из `Worker__HeartbeatPath`
  (как в контейнере) → exit 0. В каждом случае в stdout печатается причина, а не только код;
* `docker compose config` подтверждает, что healthcheck воркера — это
  `["CMD", "dotnet", "worker/Pingboard.Worker.dll", "--self-check"]` с `start_period: 30s`.

**Побочный эффект для CI:** в workflow добавлена ступень `manifests` — рендер обоих оверлеев
`kubectl kustomize` и валидация схем `kubeconform` (в CI — на каждый push и pull request,
до публикации образа). Это проверка, которой у манифестов не было: локально их можно было
только отрендерить и проверить инвариантами.

---

## 12. Ссылка на документацию API уехала из фронта в конфигурацию (пункт §10/§11)

**Что было.** В `Layout.tsx` и `LoginPage.tsx` адрес документации Scalar был захардкожен как
`http://localhost:8080/scalar/v1`. На машине разработчика это работало, на VPS и в k3d —
указывало на машину пользователя, то есть на верный 404 (или на чужой сервис). Пункт числился
открытым в §10 и §11 как P3.

**Что сделано.**

* `frontend/src/lib/env.ts` — один источник правды: `apiDocsPath` из `import.meta.env.VITE_API_DOCS_PATH`
  (пустое значение = ссылки нет вовсе);
* `Layout.tsx` и `LoginPage.tsx` рендерят ссылку только при непустом значении. Это не косметика:
  в окружении Production Api **не монтирует** `/scalar/v1` и `/openapi/v1.json` вовсе
  (`Program.cs`), поэтому ссылка была бы обещанием несуществующего маршрута;
* `deploy/Dockerfile.web` принимает build-arg `VITE_API_DOCS_PATH` (по умолчанию **пусто** —
  «безопасное» поведение); `deploy/docker-compose.yml` передаёт `/scalar/v1`, потому что
  стенд поднимается в Development и nginx контейнера проксирует документацию по тому же
  источнику, что и API;
* `deploy/docker-compose.web-prod.yml` — надстройка для Production: собирает SPA с пустым
  значением. Отдельный файл, а не переменная в `.env`: «собрать прод-фронт со ссылкой
  на документацию» — это ошибка конфигурации, а не настройка;
* CI собирает и публикует образ `web` тем же тегом, что `api`/`worker` (один релиз — один тег),
  и передаёт `/scalar/v1` — этот образ идёт на dev-стенд VPS и в k3d-dev.

**Чем проверено.** `npx tsc --noEmit` → 0; `vite build` с `VITE_API_DOCS_PATH=/scalar/v1` →
ссылка присутствует в бандле (проверено поиском по `dist/assets/*.js`); сборка без переменной →
ссылки нет; `docker compose config` для dev-набора даёт `VITE_API_DOCS_PATH: /scalar/v1`,
для `prod + web-prod` — пустую строку.

**Заодно в CI появились две ступени, которых не было:** `web` (`npm ci` → `tsc --noEmit` →
сборка → `npm audit` без падения конвейера) и образ SPA в реестре. До этого фронт проверялся
только тем, что `Dockerfile.web` собирался при деплое, то есть ошибка типов всплывала уже
на стенде.

**Остаётся открытым:** design-time строка подключения в `UptimeDbContextFactory`, распределённый
rate limit, `FOR UPDATE SKIP LOCKED`.

---

## 13. Что нужно от владельца репозитория для живых проверок

Всё, что можно было проверить без Docker-демона, кластера и GitHub, проверено (§9–§12). Дальше
начинается то, что из песочницы не делается: живой прогон конвейера и применение манифестов.
Ниже — что именно запустить и на что смотреть, чтобы первый прогон дал максимум информации.

**1. Первый прогон CI** (пуш ветки или PR; `workflow_dispatch` для выката):

* ступени `test` (90 тестов), `web` (`npm ci` → `tsc` → сборка), `manifests` (`kustomize` +
  `kubeconform`) — на каждый push и PR. Именно они ловят то, что локально не воспроизводится:
  сеть до nuget.org, `--locked-mode` на чистом runner'е, схемы Kubernetes;
* вероятные первые сбои и что они означают: `NU1004` — lock-файл разошёлся с проектом
  (лечится `dotnet restore` и коммитом); `kubeconform` на `Ingress` — версия схем старше
  `networking.k8s.io/v1`; отказ `docker login` — не выданы права `packages: write`.

**2. Локальный k8s** (`scripts/k3d-up.ps1 -LocalImages`, Docker Desktop включён):

* `kubectl -n pingboard get pods` — все ли поды дошли до `Running`, нет ли `ErrImagePull`;
* `kubectl -n pingboard logs job/migrate` — применилась ли схема (это первая проверка
  миграций на живой БД за всю историю проекта);
* `kubectl -n pingboard logs deployment/worker | head` — JSON-логи и первый пульс;
* `kubectl -n pingboard exec deployment/worker -- dotnet worker/Pingboard.Worker.dll --self-check`
  — тот же код пробы, что использует kubelet, но с человекочитаемым выводом.

**3. Выкат на VPS** (`workflow_dispatch` после настройки секретов `VPS_*` и переменных окружения):

* шаг «Выкат» печатает `docker compose ps` и проверяет `/readyz` — если он падает, значит
  либо пароль в строке подключения не совпал с `POSTGRES_PASSWORD`, либо Postgres не поднялся;
* после выката стоит один раз посмотреть `docker stats --no-stream` — это подтвердит, что
  лимиты из `docker-compose.vps.yml` подобраны верно для 1–2 ГБ (в частности, что стенд
  не ушёл целиком в swap);
* проверить `ForwardedHeaders`: в логе Api при логине должен быть виден реальный адрес клиента,
  а не адрес nginx (иначе лимит на логин общий на весь стенд — см. §5.9).

---

## 14. Выкат из реестра: миграции отдельным процессом (фактор XII)

**Что было.** Выкат на VPS (§5.9) генерировал `.env` с `MigrateOnStart=true`, то есть схему
приводил сам Api при старте. Это работало, но противоречило тому, что уже сделано в k8s (Job)
и в prod-ветке compose (§5.2): админ-процесс превращался обратно в «режим приложения» — ровно
та формулировка, которую фактор XII и разделяет.

**Что сделано.**

* `.env` на стенде генерируется с `MigrateOnStart=false`;
* выкат идёт в порядке, который compose не гарантирует сам: `pull` → ожидание `healthy`
  у Postgres (до 150 с, с выводом последних строк лога при таймауте) →
  `docker compose --profile migrate run --rm migrate` (тот же образ, команда
  `dotnet Pingboard.Api.dll --migrate`; ненулевой код останавливает выкат) → `up -d` → `/readyz`;
* единственное место, где `MigrateOnStart=true` остаётся, — dev-стенд из §4, где это осознанное
  удобство «одна команда поднимает всё». Разница зафиксирована в DEPLOY §5.9.

**Чем проверено.** YAML workflow разобран парсером; порядок шагов проверен по извлечённому
`run`-блоку: `--profile migrate run` идёт до `up -d --remove-orphans`, а `.env` содержит
`MigrateOnStart=false`. Синтаксис самого shell-блока проверить не удалось: `bash -n` в песочнице
падает на `couldn't create signal pipe` (та же граница, что у `timeout`/`date` в SANDBOX.md) —
проверка идёт на runner'е.

## 15. Мелочи k8s, которые проявляются только на живом кластере

Две правки, которые ничего не меняют в рендере, но убирают два типа редких отказов:

* **`preStop: sleep 3` у Api** ([k8s/base/30-api.yaml](k8s/base/30-api.yaml)). Между «под удалён
  из endpoints» и «Kestrel закрыл слушателя» есть окно: запрос успевает прийти в под, который
  уже не принимает, и пользователь видит 502 при rolling update. Три секунды сна до SIGTERM
  окно закрывают. Бюджет остаётся в рамках: 3 с + 10 с приложения < 30 с grace period.
* **requests у Postgres** ([k8s/base/20-postgres.yaml](k8s/base/20-postgres.yaml)). Либо и requests,
  и limits, либо QoS Burstable — при нехватке памяти на узле вытеснение начинается именно с БД,
  и это выглядит как «сервис сам перезапустился».

Проверено по рендеру: `preStop` присутствует ровно один раз (у Api), у StatefulSet postgres
есть и requests, и limits, `terminationGracePeriodSeconds` — у всех четырёх workload'ов,
включая Job миграций. Docker-демон по-прежнему недоступен из песочницы
(`permission denied ... npipe:////./pipe/dockerDesktopLinuxEngine`), поэтому живая проверка
остаётся за §13.

## 16. Откат в k8s (то, чего не было в инструкции)

Фактор V — это не только «образ с тегом», но и «на что откатываться». Для compose это уже
описано (§5.5: переключение `IMAGE_TAG`), для k8s не было ничего. Добавлено в DEPLOY §5.10:

```bash
kubectl -n pingboard get deploy -o jsonpath='{.items[*].spec.template.spec.containers[*].image}'
cd k8s/overlays/dev && kustomize edit set image ghcr.io/OWNER/pingboard-api:sha-<прошлый>
kubectl apply -k k8s/overlays/dev && kubectl -n pingboard rollout status deployment/api
```

Миграции при этом **вперёд не откатываются** (`--migrate` умеет только применять) — в инструкции
это оговорено так же, как для compose: откат схемы = восстановление из дампа или
`dotnet ef migrations script <от> <до>`, а перед обновлением с миграцией обязателен бэкап.
