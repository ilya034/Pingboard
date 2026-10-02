# Проблемы песочницы

Ограничения среды, в которой собирался каркас Pingboard. **Это не проблемы проекта** — на обычной машине с доступом в сеть ни одно из них не встретится. Здесь они собраны, потому что на них завязаны некоторые решения в репозитории (`-m:1` в командах, оффлайн-фид NuGet, in-process раннер тестов, своя замена трёх пакетов).

---

## 1. Нет доступа к nuget.org

**Симптом.** `dotnet restore` падает с `NU1301`:

```
Не удалось загрузить индекс службы для источника https://api.nuget.org/v3/index.json.
  The SSL connection could not be established, see inner exception.
  Authentication failed, see inner exception.
  В пакете безопасности отсутствуют учетные данные
```

`Invoke-RestMethod` до `api.nuget.org` обрывается («Базовое соединение закрыто»), `curl.exe` — с `schannel: AcquireCredentialsHandle failed: SEC_E_NO_CREDENTIALS`. Прокси в окружении нет.

**Важно (диагностика уточнена при сборке фронтенда, см. §8):** сеть здесь **не** выключена — падает TLS именно в Windows-стеке (Schannel).

| Клиент | Проверка | Результат |
|---|---|---|
| `node` / `npm` (OpenSSL в поставке Node) | `npm ping`, `npm install` — 56 пакетов с registry.npmjs.org | ✅ работает |
| `curl.exe` (Schannel) | HTTPS к registry.npmjs.org | ❌ `SEC_E_NO_CREDENTIALS (0x8009030e)` |
| `Invoke-WebRequest` (.NET, Schannel) | HTTPS к api.nuget.org и registry.npmjs.org | ❌ «Базовое соединение закрыто» |
| `dotnet restore` (.NET, Schannel) | api.nuget.org | ❌ `NU1301` + «В пакете безопасности отсутствуют учетные данные» |

`SEC_E_NO_CREDENTIALS` — это отказ получить учётные данные для TLS (под restricted-токеном закрыт доступ к крипто-хранилищу профиля), а не отсутствие маршрута: ошибка одинакова для **любого** хоста, поэтому и выглядит как блокировка сети. Практические следствия: оффлайн-фид NuGet по-прежнему нужен (`dotnet` в сеть не ходит), а вот проверять «доступность сайта» через `Invoke-WebRequest`/`curl.exe` в этой среде нельзя — они падают и при полностью рабочей сети.

**Обход.** `NuGet.config` указывает только на локальный фид `.packages/` — выгрузку пакетов из `%USERPROFILE%\.nuget\packages`. Фид восстанавливается скриптом:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-local-feed.ps1
```

Папка `.packages/` (~145 МБ) в git не хранится.

**Последствие для репозитория.** Трёх пакетов из §2 PLAN.md нет в кэше, поэтому они заменены (подробности — в [README.md](README.md#62-отступления-от-planmd-и-почему)):

| Пакет | Замена |
|---|---|
| `FluentValidation` | свои валидаторы на чистом C# |
| `BCrypt.Net-Next` | PBKDF2-HMAC-SHA256 из BCL |
| `prometheus-net.AspNetCore` | TODO + точная команда подключения |
| `Microsoft.Extensions.Hosting` (+ `.Logging.Console`, `.Configuration.Json`) | минимальный хост `Pingboard.Worker/Hosting/` |

---

## 2. Параллельные узлы MSBuild не работают

**Симптом.** `dotnet build`/`dotnet restore` для решения завершается вот так:

```
Ошибка сборки.
    Предупреждений: 0
    Ошибок: 0
```

Ни одной ошибки в логе, но exit code 1. Те же проекты по отдельности собираются нормально. Диагностический лог (`--verbosity diagnostic`) тоже не содержит внятной причины — только `Выполнение задачи "MSBuild" завершено с ошибкой`.

**Что проверялось.** `MSBUILDDISABLENODEREUSE=1` и `-p:RestoreDisableParallel=true` не помогают. `-m:1` (один узел) помогает **всегда**.

**Причина.** Песочница запрещает узлам MSBuild открывать каналы друг к другу и следить за процессами, поэтому многоузловая сборка не может собрать граф проектов.

**Обход.** Всегда добавлять `-m:1`:

```powershell
dotnet build backend/Pingboard.sln -m:1
dotnet restore backend/Pingboard.sln -m:1
dotnet test backend/Pingboard.sln -m:1
```

---

## 3. VSTest-хост падает при старте

**Симптом.** `dotnet test` не доходит до тестов:

```
Процесс testhost ... завершился с ошибкой. Unhandled exception.
System.ComponentModel.Win32Exception (5): Отказано в доступе.
   at System.Diagnostics.ProcessManager.OpenProcess(...)
   at System.Diagnostics.Process.EnsureWatchingForExit()
   at ...DefaultEngineInvoker.SetParentProcessExitCallback(...)
Тестовый запуск прерван.
```

**Причина.** `testhost` пытается подписаться на завершение родительского процесса, а песочница это запрещает (`OpenProcess` → Access denied). К самим тестам отношения не имеет.

**Обход.** `scripts/run-tests.ps1` запускает `dotnet test`, а при его падении переключается на `backend/scripts/TestRunner` — маленькую утилиту, которая грузит сборку тестов и вызывает `[Fact]`/`[Theory]` в своём процессе:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run-tests.ps1          # auto: dotnet test -> in-process
powershell -ExecutionPolicy Bypass -File scripts/run-tests.ps1 -Mode reflect
```

Раннер намеренно не входит в `Pingboard.sln`: это инструмент окружения, а не продукт.

---

## 4. Временные каталоги вне workspace почти недоступны

**Симптом.** `Start-Process -RedirectStandardOutput` в `%TEMP%` падает с `Win32Exception: Access is denied` (сам `Start-Process`, ещё до запуска программы). Перенаправление вывода в файл в `%TEMP%` даёт файл нулевой длины, хотя команда отработала.

**Причина.** Политика файловой песочницы — `workspace-write`: запись разрешена внутри рабочей папки, часть системных временных областей доступна лишь частично.

**Обход.** Логи и промежуточные файлы писать в рабочую папку (скрипты так и делают: `api-smoke.log`, `build.log`, `test.log` — все в `.gitignore`).

---

## 5. Доступен только Windows PowerShell 5.1

**Симптом.** `pwsh` («The term 'pwsh' is not recognized») отсутствует — есть только `powershell.exe` 5.1 (Desktop). При этом `.ps1` в UTF-8 **без BOM** ломается на кириллице:

```
Missing argument in parameter list.
The Try statement is missing its Catch or Finally block.
```

Причина — 5.1 читает файл в ANSI, пока не увидит BOM, и русский текст в сообщениях превращается в мусор, рвущий синтаксис.

**Обход.** Все `.ps1` в репозитории сохранены как UTF-8 **с BOM**, а в строках, которые парсятся (сообщения), не используются не-ASCII типографские символы вроде `→`.

**Ловушка при правке.** Инструменты записи файлов в этой среде **срезают BOM**: после правки `.ps1` через агента/редактор, который пишет UTF-8 без BOM, скрипт начинает падать синтаксически — ровно с теми сообщениями выше. Симптом: `powershell -File scripts/run-tests.ps1` ругается на `Missing closing '}'` и «The string is missing the terminator». Лечится восстановлением BOM:

```powershell
foreach ($f in Get-ChildItem scripts -Filter *.ps1) {
    $b = [System.IO.File]::ReadAllBytes($f.FullName)
    if (-not ($b[0] -eq 0xEF -and $b[1] -eq 0xBB -and $b[2] -eq 0xBF)) {
        [System.IO.File]::WriteAllBytes($f.FullName, ([byte[]](0xEF,0xBB,0xBF) + $b))
        Write-Host "BOM восстановлен: $($f.Name)"
    }
}
```

---

## 6. Скрипты репозитория не запускаются из-за ExecutionPolicy

**Симптом.** Любая документированная команда вида `powershell -File scripts/run-tests.ps1` падает **до** первой строки скрипта:

```
File ...\scripts\run-tests.ps1 cannot be loaded. The file ... is not digitally signed.
You cannot run this script on the current system.
    + FullyQualifiedErrorId : UnauthorizedAccess
```

**Причина.** Политика выполнения — `RemoteSigned` (CurrentUser и LocalMachine). Скрипты не подписаны и не имеют метки «из интернета», но restricted-токен песочницы не даёт PowerShell прочитать зону файла, и проверка политики завершается отказом. К содержимому скриптов отношения не имеет.

**Обход.** Запускать с явным `-ExecutionPolicy Bypass` или снять блокировку с файлов:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run-tests.ps1
powershell -ExecutionPolicy Bypass -File scripts/build.ps1
Get-ChildItem scripts -Filter *.ps1 | Unblock-File      # альтернатива
```

Вне песочницы на обычной машине те же команды работают и без `Bypass` — это ограничение среды.

---

## 7. Вывод дочернего процесса теряется при выходе из скрипта

**Симптом.** Скрипт отрабатывает и возвращает верный код, но **отчёта о тестах нет**:

```powershell
powershell -File scripts/run-tests.ps1 -Mode reflect
== in-process раннер (backend/scripts/TestRunner)
# ... и всё, хотя тесты прошли (exit 0)
```

**Причина.** Оболочка завершается сразу после `exit`, и stdout **нативной** команды (`dotnet`), который идёт в перенаправленный родительский поток, не успевает слиться. `Write-Host` самой оболочки печатается, а нативный вывод — нет. Вне песочницы с обычной консолью эффекта нет.

**Обход.** Прокачивать нативный вывод через командлет до `exit` — так он сливается синхронно:

```powershell
& dotnet test backend/Pingboard.sln -m:1 --nologo 2>&1 | Out-Host
```

Именно так сделано в `run-tests.ps1` и `build.ps1`.

---

## 8. npm и Vite: кэш вне workspace и `exec("net use")`

Два ограничения, которые встречаются только при работе с фронтендом. Оба лечатся флагами командной строки, а не правками кода, и **на Linux (в `deploy/Dockerfile.web`) их нет вовсе**.

### 8.1 Кэш npm лежит вне workspace

**Симптом.** `npm install` (а также `npm view`) падает сразу:

```
npm error code EPERM
npm error syscall open
npm error path C:\Users\<user>\AppData\Local\npm-cache\_cacache\tmp\***
```

**Причина.** По умолчанию кэш npm — `%LOCALAPPDATA%\npm-cache`, то есть вне рабочей папки, куда запись запрещена (та же причина, что §4).

**Обход.** Кэш внутри репозитория (`.npm-cache/`, в `.gitignore`):

```powershell
npm install --cache .npm-cache
```

Для `npm run`/`npx` флаг не нужен: они ничего не скачивают. Заметно, что `npm ping` при этом отвечает `PONG` — падает не сеть, а именно запись кэша (см. §1: с сетью у Node всё в порядке).

### 8.2 Vite вызывает `exec("net use")` — `spawn EPERM`

**Симптом.** `npm run build` (и `npm run dev`) падает ещё на загрузке конфига:

```
failed to load config from ...\frontend\vite.config.ts
[plugin externalize-deps]
Error: spawn EPERM
    at ChildProcess.spawn (node:internal/child_process:457:11)
    at Object.execFile (node:child_process:349:17)
    at optimizeSafeRealPathSync (vite/dist/node/chunks/node.js)
```

**Причина.** На Windows Vite один раз выполняет `exec("net use")` — так он строит карту сопоставленных сетевых дисков для `safeRealpathSync`. `child_process` со `stdio: 'pipe'` в песочнице запрещён (то же ограничение, из-за которого падает MSBuild, §2), и `spawn` бросает `EPERM` синхронно, до всякой сборки.

**Обход.** Загрузить конфиг нативным рантаймом Node (типы в `vite.config.ts` он срезает сам, Node ≥ 22.18):

```powershell
npm run build:sandbox     # tsc --noEmit && vite build --configLoader native
npm run dev:sandbox       # vite --configLoader native
```

Что проверялось и **не** подошло: `--configLoader runner` падает на CJS-зависимости плагина (`ReferenceError: require is not defined` в `picomatch`); обычный `bundle` упирается в тот же `spawn EPERM`. Скрипты `dev`/`build` без суффикса оставлены «каноничными» (для Linux/Docker и обычных машин), а `*:sandbox` — для этой среды.

---

## Как отключить песочницу целиком

Все ограничения выше — следствие того, что команды выполняются от restricted-токена пониженной целостности (режим DSH `workspace-write`). Режимом управляет DSH, а не этот репозиторий:

* **на сессию** — команда `/permission` в чате (пресет `danger-full-access`);
* **по умолчанию для новых сессий** — страница **Настройки → Разрешения**, либо переменная `DSH_PERMISSION_MODE=danger-full-access` на процесс DSH.

`danger-full-access` снимает ACL-restricted-токен: команды идут от обычного токена, и тогда исчезают §1 (сеть), §3 (`testhost`), §4 (запись вне workspace), §6 и §7. `-m:1` (§2) при этом остаётся нужен только если канал к узлам MSBuild всё равно недоступен. Цена — сборка и тесты перестают быть изолированными, а политика подтверждений становится `never`.

---

## Сводка

| # | Ограничение | Обход в репозитории |
|---|---|---|
| 1 | nuget.org недоступен из процессов | локальный фид `.packages` + `scripts/build-local-feed.ps1` |
| 2 | параллельные узлы MSBuild | всегда `-m:1` |
| 3 | `testhost` падает (`OpenProcess`) | `scripts/run-tests.ps1` → in-process `backend/scripts/TestRunner` |
| 4 | запись вне workspace | логи и артефакты в рабочей папке |
| 5 | только PowerShell 5.1, нужен BOM | `.ps1` в UTF-8 с BOM, без не-ASCII пунктуации в коде |
| 6 | ExecutionPolicy блокирует `.ps1` | запускать с `-ExecutionPolicy Bypass` |
| 7 | теряется stdout нативных команд | прокачивать вывод через `2>&1 \| Out-Host` до `exit` |
| 8 | кэш npm пишется вне workspace | `npm install --cache .npm-cache` (кэш в `.gitignore`) |
| 9 | Vite делает `exec("net use")` → `spawn EPERM` | `npm run dev:sandbox` / `npm run build:sandbox` (`--configLoader native`) |

### Механика: почему это всё связано

Команды в песочнице DSH запускаются от **restricted-токена** (`WRITE_RESTRICTED` + понижение целостности до Low) с capability-SID на запись в workspace и приватный temp. Все семь пунктов — следствия этого одного механизма:

* **Проверка доступа идёт дважды** (обычные SID + restricting SID), поэтому узлы MSBuild не могут открыть канал друг к другу, а `testhost` — описатель родительского процесса (§2, §3).
* **Ограничения по SID ломают Windows-клиенты TLS.** Сеть доступна: `node`/`npm` из песочницы скачали 56 пакетов с registry.npmjs.org, а `node -e "https.get(...)"` отвечает 200. А вот `curl.exe` и любой .NET-клиент (`dotnet`, `Invoke-WebRequest`) падают с `SEC_E_NO_CREDENTIALS`: под restricted-токеном Schannel не получает учётные данные для TLS, и ошибка одинакова для любого хоста — поэтому её легко принять за блокировку сети. Практическое следствие ровно одно: оффлайн-фид NuGet нужен не потому, что «сети нет», а потому что `dotnet` в неё не может (см. §1). Отдельно любопытно, что это **не** заявленное свойство песочницы: документация ACL-backend прямо говорит, что сеть и видимость процессов им **не** ограничиваются («writes and deletes are restricted; reads, network, and process visibility are not»), — и по факту так оно и есть.
* **Запись разрешена только в workspace и приватный temp** — отсюда §4.
* **Урезанный токен ломает проверку зоны файла в PowerShell** и не даёт нативному stdout слиться до `exit` (§6, §7).

Практический вывод: пункты §1, §3, §4, §6, §7 — это **одна** причина (restricted-токен), и она снимается одним переключателем режима DSH, а не правками в репозитории. Обходы в репозитории нужны только чтобы работать **не выходя** из песочницы.
