# Pingboard — развёртывание

Практическая инструкция: как поднять стенд локально (§4) и как выкатить его на VPS (§5). Всё, что
отличается между окружениями, приходит переменными окружения — код и образы те же самые
(факторы III и X — [PLAN.md §10](PLAN.md#10-двенадцать-факторов--конкретные-решения)).

Сопутствующие документы: [README.md](README.md) — что это за проект, API, конфигурация;
[PLAN.md](PLAN.md) — план и вехи; [SANDBOX.md](SANDBOX.md) — ограничения машины, на которой
каркас собирался (нужны только при работе в песочнице DSH).

---

## 1. Что разворачивается

| Сервис | Из чего собирается | Внутренний порт | Наружу | Роль |
|---|---|---|---|---|
| `postgres` | образ `postgres:18-alpine` | 5432 | **нет** (только сеть compose) | БД; данные — в томе `pgdata` (внутри подкаталог `18/docker`) |
| `api` | `deploy/Dockerfile.api` | 8080 | `API_BIND` (`0.0.0.0:8080` на dev, `127.0.0.1:8080` в проде) | REST API, `/healthz`, `/readyz` |
| `worker` | **тот же образ**, другая команда | — | нет | цикл HTTP-проверок; healthcheck по файлу-пульсу |
| `web` | `deploy/Dockerfile.web` (node-сборка → `nginx:alpine`) | 80 | `WEB_BIND` (`8081` на dev) | статика SPA + прокси `/api` на `api:8080` |
| `migrate` | тот же образ, команда `--migrate` | — | нет | one-off процесс: применить схему и выйти (профиль `migrate`) |

Поток запроса: браузер → `web:80` (nginx) → `/api/*` → `api:8080` → `postgres:5432`.
Воркер ходит в БД и по проверяемым URL, HTTP-порт ему не нужен — поэтому его «здоровье»
измеряется свежестью файла-пульса.

Ключевая мысль про `api` и `worker`: это **один образ и два process type** (фактор VIII), поэтому
второй процесс не разъезжается с первым по версии, а масштабируются они независимо. Образ у них общий
и тегированный (`IMAGE_TAG`, фактор V), а реплики Api требуют снять публикацию порта — для этого есть
надстройка `deploy/docker-compose.scale.yml`, см. §5.7.

---

## 2. Требования к машине

| Что | Значение |
|---|---|
| Docker | Engine 24+ с compose v2 (`docker compose version`) |
| RAM | 2 ГБ (сами контейнеры едят ~150–200 МБ, но образы SDK/node тяжёлые при сборке) |
| Диск | ~4 ГБ (образы SDK 10.0, node 22, nginx, postgres + данные) |
| Сеть на этапе сборки | `mcr.microsoft.com`, `registry.npmjs.org`, `nuget.org`, Docker Hub — иначе базовые образы и пакеты не вытянуть |
| .NET SDK / Node на хосте | **не нужны**: всё собирается внутри образов |
| Порты на VPS | наружу 22/80/443; 5432 и 8080/8081 — **только loopback или закрыты** |
| Дополнительно для прода | домен, указывающий на VPS, и TLS-терминатор (Caddy/nginx/облачный LB) |

Проверка готовности хоста одной командой:

```bash
docker compose version && docker info --format '{{.ServerVersion}} RAM:{{.MemTotal}}'
```

---

## 3. Конфигурация: `.env`

```bash
cp .env.example .env
openssl rand -base64 48      # → положить в Jwt__Secret (минимум 32 байта)
```

Файл `.env` в git не попадает; в репозитории лежит только шаблон `.env.example` с теми же
именами переменных (фактор III). Ниже — что действительно важно при развёртывании.

| Переменная | dev-стенд | прод | Зачем |
|---|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Development` | `Production` | вне Development требуются реальные секреты и запрещён сид демо-учётки |
| `MigrateOnStart` | `true` | `false` | в проде схему приводит отдельный процесс `migrate` (фактор XII), а не сам Api |
| `SeedOnStart` | не задавать (по умолчанию `true` в Development) | **не задавать** | старт с `SeedOnStart=true` вне Development падает с объяснением: пароль демо-учётки известен из исходников |
| `POSTGRES_USER` / `POSTGRES_PASSWORD` / `POSTGRES_DB` | значения на ваш вкус | сильный пароль | их использует контейнер `postgres` |
| `ConnectionStrings__Default` | `Host=postgres;...` | то же, пароль **обязан совпадать** с `POSTGRES_PASSWORD` | хост `postgres` — имя сервиса в сети compose |
| `Jwt__Secret` | можно пусто | обязателен | в Development генерируется эфемерный ключ; вне Development пустой/короткий секрет роняет старт. Смена секрета = все выданные токены недействительны |
| `Jwt__ExpiresMinutes` | `120` | `120` | срок жизни access-токена |
| `RateLimit__LoginPermitLimit` / `RegisterPermitLimit` / `AuthWindowSeconds` | `20` / `10` / `60` | так же | лимит на `/api/auth/*` (каждый запрос считает PBKDF2 — это ещё и защита CPU) |
| `ForwardedHeaders__Enabled` + `KnownProxies`/`KnownNetworks` | `false` | `true` + сеть прокси | см. §5.4: без этого лимит на логин считается по адресу прокси |
| `Probe__TimeoutMs` | `5000` | `5000` | таймаут одной попытки проверки |
| `Probe__AllowPrivateNetworks` | `false` | `false` | `true` только на доверенном стенде: снимает барьер SSRF и открывает проверку внутренних адресов |
| `Worker__TickSeconds` / `BatchSize` / `MaxParallel` | `5` / `100` / `8` | так же | период цикла, размер батча, параллелизм проверок |
| `Worker__HeartbeatPath` | `/tmp/pingboard-worker-heartbeat` | так же | файл-пульс; путь захардкожен в healthcheck compose — меняя его, правьте и там |
| `Monitors__UptimeWindowHours` / `UptimeBarSegments` / `DashboardHistoryPerMonitor` | `24` / `24` / `500` | так же | окно и сегменты полосы доступности, глубина истории на дашборде |
| `Cors__Origins__0` | `http://localhost:5173` | не важно | нужно только dev-фронту Vite; в проде SPA и API на одном origin, CORS не участвует |
| `API_BIND` | `0.0.0.0:8080` | `127.0.0.1:8080` | куда публикуется Api |
| `WEB_BIND` | `8081` | `127.0.0.1:8081` | куда публикуется nginx со SPA |
| `PGDATA_PATH` (только prod-файл) | — | `/srv/pingboard/pgdata` | каталог хоста под данные Postgres |
| `IMAGE_TAG` | обычно не задавать (будет `local`) | тег релиза, например `0.1.0` | тег единого образа api/worker/migrate (фактор V): по нему видно, что закатано, и на него же делается откат |
| `APP_VERSION` | `0.1.0` | та же, что в `Directory.Build.props` | уходит в OCI-label образа: `docker image inspect pingboard-api:<тег>` показывает версию внутри артефакта, а не только в git |

Две ловушки, которые стоит знать заранее:

* **`API_BIND` и `WEB_BIND` не должны совпадать.** Оба по умолчанию разные (8080 и 8081), но
  популярная ошибка — выставить `API_BIND=127.0.0.1:8081`: compose упадёт с
  `port is already allocated`, потому что 8081 занят `web`.
* **`--env-file` обязателен.** Подстановка `${...}` в самом compose-файле читает `.env` из
  каталога compose-файла (`deploy/`), а не из корня репозитория. Если запустить без флага,
  контейнер `postgres` получит пароль-умолчание, а Api — строку подключения из `.env`, и Api
  не сможет подключиться к БД. Все команды ниже поэтому выполняются **из корня репозитория**
  с явным `--env-file .env`.

Проверить, что подстановка сработала, можно без запуска контейнеров:

```bash
docker compose --env-file .env -f deploy/docker-compose.yml config | grep -E 'POSTGRES_|ConnectionStrings|API_BIND|WEB_BIND'
```

Отдельно про lock-файлы NuGet (фактор II): рядом с каждым `.csproj` лежит `packages.lock.json`,
а restore в образе идёт с `--locked-mode`. Поэтому на хосте ничего делать не нужно — но если вы
меняете зависимости (`Directory.Packages.props` или `PackageReference`), lock-файл обязан
обновиться в том же коммите:

```bash
dotnet restore backend/Pingboard.sln        # пересобирает packages.lock.json
git add backend/**/packages.lock.json
```

Если этого не сделать, сборка образа упадёт с внятной ошибкой `NU1004` («ссылки на пакеты
изменились») — это и есть цель: «на CI собралось не то, что проверено» ловится до сборки, а не
после деплоя. Намеренно обновить граф в обход проверки: `dotnet restore backend/Pingboard.sln --force-evaluate`.

---

## 4. Dev-стенд за пять минут

Из корня репозитория:

```bash
cp .env.example .env
# Jwt__Secret можно не заполнять: в Development Api сгенерирует эфемерный ключ на запуск
docker compose --env-file .env -f deploy/docker-compose.yml up -d --build
docker compose --env-file .env -f deploy/docker-compose.yml ps
```

Что произойдёт: соберутся образы `api` (SDK → aspnet) и поднимутся `postgres` + `api` + `worker`.
Api сам применит миграции (`MigrateOnStart=true`) и создаст демо-учётку
(`demo@pingboard.local` / `demo-password`).

Проверка:

```bash
curl -fsS http://localhost:8080/healthz          # {"status":"ok"}
curl -fsS http://localhost:8080/readyz           # {"status":"ready","database":"ok"}
```

SPA — отдельным профилем (чтобы обычный `up` не собирал node-образ, если нужен только API):

```bash
docker compose --env-file .env -f deploy/docker-compose.yml --profile web up -d --build
# → http://localhost:8081  (вход демо-учёткой)
```

Добавьте монитор на `https://example.com` и на `https://httpstat.us/500` — через минуту-две в
дашборде будут зелёный и красный бейджи, полоса доступности и история проверок.

Полезные команды стенда:

```bash
docker compose --env-file .env -f deploy/docker-compose.yml logs -f api worker
docker compose --env-file .env -f deploy/docker-compose.yml restart worker   # данные не теряются (фактор VI)
docker compose --env-file .env -f deploy/docker-compose.yml stop api         # graceful shutdown (фактор IX)
docker compose --env-file .env -f deploy/docker-compose.yml down             # без -v: том pgdata остаётся
```

---

## 5. «Прод» на VPS

### 5.1. Подготовка хоста

```bash
sudo mkdir -p /srv/pingboard && sudo chown "$USER" /srv/pingboard
cd /srv/pingboard
git clone <адрес-репозитория> app && cd app      # или rsync/scp, если репозитория нет
cp .env.example .env && nano .env                # §3: Production, секреты, API_BIND, WEB_BIND
mkdir -p /srv/pingboard/pgdata                   # PGDATA_PATH из .env (внутри появится 18/docker/…)
```

Правки в `.env` под прод (минимальный набор):

```dotenv
ASPNETCORE_ENVIRONMENT=Production
MigrateOnStart=false
Jwt__Secret=<openssl rand -base64 48>
POSTGRES_PASSWORD=<сильный пароль>
ConnectionStrings__Default=Host=postgres;Port=5432;Database=pingboard;Username=pingboard;Password=<тот же пароль>
API_BIND=127.0.0.1:8080
WEB_BIND=127.0.0.1:8081
PGDATA_PATH=/srv/pingboard/pgdata
```

Проверьте firewall: наружу открыты 22/80/443; 5432, 8080 и 8081 — закрыты (Api и nginx и так
слушают только loopback, но правило лишним не будет).

Дальше все команды удобно запускать через переменную:

```bash
export DC="docker compose --env-file .env -f deploy/docker-compose.yml -f deploy/docker-compose.prod.yml"
$DC config >/dev/null && echo 'конфигурация и подстановка .env в порядке'
```

### 5.2. Первый деплой

```bash
$DC --profile web build                     # образ api (в нём же worker) и образ web
$DC --profile migrate run --rm migrate      # применить схему и выйти (фактор XII)
$DC --profile web up -d                     # postgres + api + worker + web(nginx)
$DC ps                                      # все сервисы Up (healthy)
```

`--profile web` нужен и на сборке: compose пропускает сервисы неактивных профилей, поэтому без
него образ nginx со SPA просто не соберётся (при `up` он соберётся сам, но лучше видеть ошибку
сборки сразу).

Порядок здесь не косметический: в проде `MigrateOnStart=false`, поэтому если сначала поднять
Api на пустой БД, он будет отвечать 500/503 на запросы к данным. Схему приводит отдельный
процесс `migrate` — он стартует только когда Postgres уже healthy и завершается с кодом 0.

Автозапуск после перезагрузки хоста обеспечивают `restart: unless-stopped` у всех долгоживущих
сервисов: Docker поднимет их сам при старте демона. У `migrate` `restart: "no"` — это one-off.

### 5.3. Приёмка

```bash
# 1. Api жив и готов (напрямую с хоста, минуя прокси)
curl -fsS http://127.0.0.1:8080/healthz      # {"status":"ok"}
curl -fsS http://127.0.0.1:8080/readyz       # {"status":"ready","database":"ok"}

# 2. nginx отдаёт SPA и проксирует /api
curl -fsS -o /dev/null -w 'SPA:%{http_code}\n' http://127.0.0.1:8081/
curl -fsS -o /dev/null -w 'API via nginx:%{http_code}\n' http://127.0.0.1:8081/api/monitors   # 401 — это правильно

# 3. Пользователь (в проде демо-учётки нет — регистрируемся)
TOKEN=$(curl -sS -X POST http://127.0.0.1:8080/api/auth/register \
  -H 'Content-Type: application/json' \
  -d '{"email":"admin@example.com","password":"strong-password-1"}' \
  | sed -E 's/.*"accessToken":"([^"]+)".*/\1/')
echo "token length: ${#TOKEN}"

# 4. CRUD
curl -sS http://127.0.0.1:8080/api/monitors -H "Authorization: Bearer $TOKEN"     # []
curl -sS -X POST http://127.0.0.1:8080/api/monitors \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"name":"example","url":"https://example.com","intervalSeconds":60,"enabled":true}'

# 5. Воркер действительно пишет историю (через 1–2 минуты)
curl -sS "http://127.0.0.1:8080/api/monitors/<id>/checks?limit=5" -H "Authorization: Bearer $TOKEN"
$DC exec -T postgres psql -U pingboard -d pingboard -c 'select count(*), max(checked_at) from checks;'

# 6. Публичный адрес (после §5.4)
curl -fsS https://<домен>/healthz
```

`psql -U pingboard -d pingboard` — значения по умолчанию из `.env.example`; если меняли
`POSTGRES_USER`/`POSTGRES_DB`, подставьте свои. Если регистрация вернула 429 — сработал лимит
(10 запросов на адрес в минуту): подождите минуту или поднимите `RateLimit__RegisterPermitLimit`.

### 5.4. TLS и внешний прокси

Схема: интернет → 443 (TLS-терминатор на хосте) → `127.0.0.1:8081` (`web`, nginx в контейнере)
→ `api:8080` внутри сети compose. SPA и API оказываются на одном origin, поэтому CORS в проде
не участвует, а `/api` уже проксируется внутренним nginx.

Самый короткий вариант — Caddy на хосте:

```caddyfile
pingboard.example.com {
    reverse_proxy 127.0.0.1:8081
}
```

Он сам получит сертификат Let's Encrypt и передаст `X-Forwarded-For`/`X-Forwarded-Proto`.

Теперь про то, зачем нужны `ForwardedHeaders__*`. Ключ раздела rate limit — адрес соединения,
а им для Api оказывается внутренний nginx (`web`). Без разбора `X-Forwarded-For` все клиенты
попадут в один раздел, и лимит на логин станет общим на весь стенд. Включать разбор можно
**только** там, где Api гарантированно недоступен напрямую (у нас `API_BIND=127.0.0.1:8080`,
снаружи его нет):

```dotenv
ForwardedHeaders__Enabled=true
ForwardedHeaders__KnownNetworks__0=172.16.0.0/12     # сеть compose
```

Если включить это при опубликованном наружу Api, клиент подставит себе `X-Forwarded-For` и
обойдёт лимит перебором заголовка — поэтому по умолчанию настройка выключена.

Проверка, что всё сошлось:

```bash
curl -fsS https://<домен>/readyz                     # {"status":"ready","database":"ok"}
curl -s -o /dev/null -w '%{http_code}\n' https://<домен>/api/monitors   # 401
# и один раз посмотреть в лог, что 429 приходит с реальными адресами, а не с одного:
$DC logs --since 5m api | grep -i 'rate\|429' || true
```

`/healthz` и `/readyz` специально проксируются наружу (`location = /healthz`, `= /readyz` в
`deploy/nginx.conf`) — иначе они попали бы в SPA-обработчик и отдавали `index.html` с кодом 200,
то есть внешний мониторинг всегда видел бы «всё хорошо». Публичная проверка готовности
показывает только `ok`/`unavailable`, никаких данных из БД.

### 5.5. Обновление версии и откат

```bash
cd /srv/pingboard/app
git pull
export IMAGE_TAG=0.1.0                      # тег релиза; APP_VERSION — в .env (см. §3)
$DC --profile web build                     # образ api/worker/migrate под этим тегом + образ web
$DC --profile migrate run --rm migrate      # схема — до старта нового кода
$DC --profile web up -d                     # пересоздаст контейнеры на новом образе
$DC ps && curl -fsS http://127.0.0.1:8080/readyz
docker image inspect pingboard-api:0.1.0 \
  --format '{{ index .Config.Labels "org.opencontainers.image.version" }}'   # → 0.1.0
```

Тег здесь не украшение: без него «релиз» — это «пересобрали тот же коммит», и откатываться
некуда, кроме `git checkout` и новой сборки. С тегом на хосте остаётся список артефактов
(`docker image ls pingboard-api`), а откат кода становится переключением образа:

```bash
IMAGE_TAG=0.0.9 $DC --profile web up -d     # старая версия из уже собранного образа, без сборки
```

Порядок «сначала миграции, потом код» рассчитан на совместимые изменения (добавить таблицу или
колонку). Ломающие изменения (переименование, удаление колонки) в один шаг не укладываются —
нужен приём expand/contract: сначала релиз, который пишет и старое, и новое, потом удаление
старого. Для учебного стенда достаточно первого варианта.

Откат по исходникам (если образа прошлого релиза на хосте нет):

```bash
git checkout <прошлый-тег-или-коммит>
$DC build && $DC --profile web up -d
```

Важно: `--migrate` умеет только применять миграции, обратно он их не откатывает. Если релиз
включал изменение схемы, откат кода без отката схемы может не заработать; откат схемы — это
либо скрипт `dotnet ef migrations script <от> <до>` (нужен SDK), либо восстановление из дампа
(§5.6). Поэтому бэкап перед обновлением с миграцией — обязателен.

### 5.6. Бэкапы

Данные лежат в `pgdata` на хосте (`/srv/pingboard/pgdata`, внутри — подкаталог `18/docker`), но логический дамп надёжнее
снапшота каталога: он не зависит от версии сервера и снимается на живой БД.

```bash
# дамп (в cron — например, ежедневно в 3:30)
$DC exec -T postgres pg_dump -U pingboard -d pingboard -Fc > /srv/pingboard/backup-$(date +%F).dump

# восстановление в существующую БД
$DC exec -T postgres pg_restore -U pingboard -d pingboard --clean --if-exists < /srv/pingboard/backup-2026-10-02.dump
```

Проверять восстановление нужно заранее, а не в момент аварии: разверните дамп в отдельную БД
(`createdb` + `pg_restore -d`) и убедитесь, что таблицы `users`/`monitors`/`checks` на месте.

Одна эксплуатационная деталь: таблица `checks` растёт без ограничения — удаление старых
проверок (retention) в план вынесено как расширение №7 (§12 PLAN.md), на MVP его нет. Для
стенда это неважно, для долгой работы — либо включите расширение, либо следите за размером:

```bash
$DC exec -T postgres psql -U pingboard -d pingboard -c \
  "select pg_size_pretty(pg_total_relation_size('checks')) as checks_size;"
```

### 5.7. Эксплуатация

| Задача | Команда / как |
|---|---|
| Логи | `$DC logs -f api worker` — JSON в stdout (фактор XI); `traceId` из ошибки в браузере ищется здесь же |
| Здоровье Api | healthcheck `curl -fsS /healthz`, интервал 10 с |
| Здоровье воркера | свежесть файла-пульса (`< 120 с`); воркер пишет его даже когда все проверки падают |
| Здоровье БД | `pg_isready` + `/readyz` |
| Масштабирование Api | Реплики поднимаются надстройкой `deploy/docker-compose.scale.yml`, которая снимает публикацию фиксированного порта (`ports: !reset []`, нужен compose ≥ 2.24): `export DCS="docker compose --env-file .env -f deploy/docker-compose.yml -f deploy/docker-compose.scale.yml"`, затем `$DCS --profile web up -d --scale api=2` и `$DCS --profile web exec web nginx -s reload` — nginx резолвит `api:8080` один раз при старте и новые реплики сам не подхватывает. На «проде» в список файлов добавляется `-f deploy/docker-compose.prod.yml`. Api остаётся виден через nginx (`curl -fsS http://localhost:8081/healthz`), конкретная реплика проверяется изнутри: `$DCS exec api curl -fsS localhost:8080/healthz`. Обязательное условие — **заданный `Jwt__Secret`**: с пустым секретом каждая реплика генерирует свой эфемерный ключ подписи, и токен, выданный одной репликой, вторая отвергнет с 401 |
| Лимиты при репликах Api | Rate limit — в памяти процесса, у каждой реплики свой счётчик: лимит умножается на число реплик (а за nginx все клиенты приходят с адреса прокси, и лимит становится общим на стенд). Осознанная граница, распределённый счётчик — M5, см. README §6.5 |
| Масштабирование воркера | `$DC up -d --scale worker=2` — дублей проверок не будет: `ListDueAsync` (`MonitorRepository`) захватывает мониторы условным `UPDATE … WHERE last_checked_at + interval <= now` и отдаёт только строки со своим штампом, поэтому сосед получает пустую выборку. Цена захвата: процесс, умерший сразу после него, теряет один интервал по этим мониторам — снимает эту цену `FOR UPDATE SKIP LOCKED` (расширение №6), а не дубли |
| Пульс при репликах воркера | `Worker__HeartbeatPath=/tmp/pingboard-worker-heartbeat` — файл внутри контейнера, тома нет, поэтому у каждой реплики свой пульс и свой healthcheck |
| Метрики | пока нет, `/metrics` — веха M5; наблюдаемость сейчас — логи + healthcheck'и |
| Смена `Jwt__Secret` | все выданные токены становятся недействительными (пользователи перелогинятся) |
| Обновление базовых образов | `$DC pull && $DC --profile web up -d` (postgres/nginx/node) |
| Воркер после рестарта | «расписание» — данные в БД, поэтому `restart worker` безопасен (факторы VI, IX) |
| Остановка контейнеров | `$DC stop api worker` → SIGTERM, приложение доигрывает в пределах своего бюджета в 10 с (`HostOptions.ShutdownTimeout` у Api, `WorkerHost.ShutdownTimeout` у воркера). В compose у `api` и `worker` стоит `stop_grace_period: 15s`: без запаса Docker прислал бы SIGKILL ровно в момент доигрывания (фактор IX) |
| Что закатано сейчас | `docker image ls pingboard-api` и `docker image inspect pingboard-api:<тег> --format '{{ index .Config.Labels "org.opencontainers.image.version" }}'`; тег задаётся `IMAGE_TAG` при сборке релиза (фактор V, §5.5) |

Оговорка про запуски: ни команды масштабирования, ни команды обновления/отката на машине сборки
**не прогонялись** (Docker-демон недоступен, см. README §7) — сказанное выше выведено из конфигурации
и кода. Что проверено статически: `docker compose config` проходит для всех трёх наборов файлов
(базовый, с `docker-compose.scale.yml`, с `docker-compose.prod.yml` + профиль `migrate`) и подтверждает,
что `!reset []` снимает у `api` маппинг `8080:8080`, что `image: pingboard-api:<тег>`,
`stop_grace_period: 15s` и `APP_VERSION` попадают в итоговый конфиг у `api`, `worker` и `migrate`;
в Api нет `IHostedService`, а процесса-локальное состояние ограничено счётчиками rate limit и
эфемерным dev-ключом подписи; захват в `ListDueAsync` — условный `ExecuteUpdateAsync`, и его SQL
покрыт контрактным тестом (`DatabaseContractTests`).

### 5.8. Диагностика

| Симптом | Причина | Что делать |
|---|---|---|
| `dependency failed to start: container …postgres… is unhealthy`, в логах `Error: in 18+, these Docker images are configured to store database data in a format which is compatible with "pg_ctlcluster"` | том Postgres смонтирован по старому пути `/var/lib/postgresql/data`, а в `postgres:18` PGDATA версионный: `/var/lib/postgresql/18/docker`, и объявленный `VOLUME` — `/var/lib/postgresql`. Entrypoint видит монтирование по старому пути и выходит с кодом 1 (до `initdb`, поэтому повреждённых данных там нет) | монтировать том в `/var/lib/postgresql` (в `deploy/docker-compose.yml` и prod-файле это уже так), затем `down -v` → `up -d` (том без данных, терять нечего). Либо откатиться на `postgres:17-alpine` и оставить старый путь — но тогда правьте тег в обоих compose-файлах |
| `postgres` unhealthy без сообщения выше | не поднялся `initdb`: пустой `POSTGRES_PASSWORD`, нет места, права на хостовый каталог `PGDATA_PATH` | `$DC logs postgres` — в конце лога видна причина; каталог на хосте должен принадлежать вашему пользователю (в контейнере это `postgres` с uid 999, Docker сам делает `chown`) |
| `port is already allocated` | `API_BIND` и `WEB_BIND` на одном порту (частый случай — оба 8081) | развести: Api `127.0.0.1:8080`, web `127.0.0.1:8081` |
| Api стартует, но все запросы к данным — 500/503 | миграции не применены (`MigrateOnStart=false`) | `$DC --profile migrate run --rm migrate` |
| `/readyz` → 503 `database:unavailable` | Postgres не поднялся, либо пароль в `ConnectionStrings__Default` не совпал с `POSTGRES_PASSWORD` | `$DC logs postgres`, сверить пароли в `.env` |
| SPA открывается, все запросы → 401 | токен просрочен/сменился `Jwt__Secret` | войти заново; на dev-стенде секрет эфемерный и меняется при рестарте Api |
| `/login` работает, но F5 на внутреннем маршруте даёт 404 | не применился `try_files $uri /index.html` | проверить `deploy/nginx.conf` в контейнере `web` |
| 429 приходит «на всех» | за прокси выключен разбор `X-Forwarded-For` | §5.4: `ForwardedHeaders__Enabled=true` + сеть прокси |
| Воркер `unhealthy`, в логах `tick failed` | БД недоступна или все проверки валятся | это не обязательно смерть цикла: он продолжает итерации, смотрите `$DC logs worker` |
| Логин отдаёт 503 | Api жив, но БД недоступна — это осознанный контракт (503 = backing service, не 500) | `$DC ps`, `/readyz`, логи postgres |
| Сборка образа: `failed to compute cache key: ...Nupkg/NuGet.config` | старая версия `deploy/Dockerfile.api` (ссылалась на файлы, которых нет в `backend/`) | обновить репозиторий: пути исправлены, restore в образе идёт в nuget.org |
| Сборка образа: ошибки restore NuGet | нет доступа к `nuget.org` из сборочной среды | обеспечить сеть или собрать образ на машине с сетью и перенести его (`docker save`/`load`) |

Аварийный минимум при непонятном поведении:

```bash
$DC ps
$DC logs --since 10m api worker postgres
curl -i http://127.0.0.1:8080/readyz
```

---

## 6. Без Docker (только для разработки)

Боевое развёртывание — это compose из §5; локальный запуск без контейнеров нужен, чтобы
отлаживать код. Нужны .NET 10 SDK, Node 22+ и Postgres 16+ (проще всего сам Postgres поднять
контейнером). Подробно — [README §3.1](README.md#31-локально-без-docker) (Api и воркер) и
[README §3.4](README.md#34-фронтенд-spa) (SPA).

```bash
docker run --name pingboard-pg -e POSTGRES_USER=pingboard -e POSTGRES_PASSWORD=pingboard \
  -e POSTGRES_DB=pingboard -p 5432:5432 -d postgres:18-alpine

dotnet run --project backend/src/Pingboard.Api -m:1        # http://localhost:8080
dotnet run --project backend/src/Pingboard.Worker -m:1     # второй процесс

cd frontend && npm install && npm run dev                  # http://localhost:5173 (прокси /api → :8080)
```

Одна оговорка про restore на чистой машине: в репозитории `NuGet.config` оставлен в
«оффлайн-режиме» — источник только локальный фид `.packages`, а каталог в git не хранится.
На машине с интернетом раскомментируйте в `NuGet.config` источник `nuget.org` (строка уже
подготовлена) или разово передайте его в команду:

```bash
dotnet restore backend/Pingboard.sln -m:1 --source https://api.nuget.org/v3/index.json
```

В песочнице DSH к этому добавляются свои ограничения (кэш npm, `--configLoader native` у Vite,
`-m:1` у MSBuild) — они разобраны в [SANDBOX.md](SANDBOX.md), а быстро собрать SPA там помогает
`scripts/build-web.ps1`.

---

## 7. Шпаргалка

```bash
# dev-стенд
docker compose --env-file .env -f deploy/docker-compose.yml up -d --build
docker compose --env-file .env -f deploy/docker-compose.yml --profile web up -d --build

# прод
export DC="docker compose --env-file .env -f deploy/docker-compose.yml -f deploy/docker-compose.prod.yml"
$DC --profile web build
$DC --profile migrate run --rm migrate
$DC --profile web up -d
$DC ps && curl -fsS http://127.0.0.1:8080/readyz

# бэкап
$DC exec -T postgres pg_dump -U pingboard -d pingboard -Fc > backup-$(date +%F).dump
```

---

## 8. Что в этой инструкции не проверено

Честная граница: **образы ни разу не собирались** — на машине, где писался каркас, Docker Desktop
не запущен, а до реестра образов нет сети (см. [README §1](README.md#1-состояние) и
[SANDBOX.md §1](SANDBOX.md#1-нет-доступа-к-nugetorg)). То есть:

* команды и конфигурация выверены по файлам репозитория, но не прогонялись на живом Docker;
* сквозной сценарий «логин → монитор → воркер пишет `checks`» на живой БД не прогонялся ни разу
  (нет работающего Postgres) — это остаётся главным непроверенным местом всего проекта;
* при написании инструкции нашлись и исправлены четыре дефекта, которые как раз мешали бы
  развёртыванию: `deploy/Dockerfile.api` копировал несуществующие файлы (сборка образа падала на
  первом же `COPY`) и полагался на оффлайн-фид, которого в образе нет; пример `API_BIND` в
  `.env.example` занимал порт `web` (8081); команды с `--env-file ../.env` не работали ни из
  корня, ни из `deploy/`; отсутствовал `.dockerignore` (в Linux-образ SPA попадали бы
  `node_modules`, собранные под Windows). Плюс `/readyz` теперь проксируется nginx'ом наружу;
* пятый дефект нашёлся на первом же реальном запуске стенда — том Postgres монтировался по пути
  `/var/lib/postgresql/data`, который перестал быть PGDATA в `postgres:18` (см. первую строку
  таблицы §5.8). Именно поэтому раздел «Диагностика» стоит читать до первого `up`, а не после.

Поэтому первый запуск стоит делать по шагам §5, с проверками после каждого шага — именно в этом
порядке дефекты вида «не тот путь в Dockerfile» находятся за секунды, а не после долгой отладки
пустого дашборда.
