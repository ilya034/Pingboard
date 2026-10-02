<#
.SYNOPSIS
    Проверяет, что Api поднимается и отвечает на служебные эндпоинты.

.DESCRIPTION
    Запускает Pingboard.Api без БД (MigrateOnStart=false), ждёт /healthz,
    затем читает /openapi/v1.json и проверяет, что все маршруты из §5 PLAN.md объявлены.
    /readyz в этом режиме ожидаемо отвечает 503 — это и есть разница между liveness и readiness.

    Отдельно проверяется JWT-обвязка (§ M4): маршруты мониторов без токена дают 401,
    валидный токен (выписанный тем же секретом, что и у Api) принимается, а токен
    с чужой подписью — отвергается. Это единственная проверка, которая ловит расхождение
    между выдачей токена и его валидацией, не требуя живой БД.

.EXAMPLE
    powershell -File scripts/smoke-api.ps1
#>
[CmdletBinding()]
param(
    [int] $Port = 8099,
    [int] $TimeoutSeconds = 90
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$baseUrl = "http://localhost:$Port"

$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = "http://localhost:$Port"
$env:ConnectionStrings__Default = 'Host=localhost;Port=5432;Database=pingboard;Username=pingboard;Password=pingboard;Timeout=3'
$env:MigrateOnStart = 'false'

# Фиксированный секрет подписи: с ним smoke-скрипт может выписать токен так же,
# как это делает Api, и убедиться, что схема его принимает.
$env:Jwt__Secret = 'smoke-test-secret-value-0123456789abcdef'

$logPath = Join-Path $root 'api-smoke.log'
if (Test-Path $logPath) { Remove-Item $logPath -Force }

$env:SeedOnStart = 'false'

# Маленький лимит на /api/auth/register: так срабатывание 429 проверяется несколькими
# запросами, а не сотней. Ключ тот же, что и в проде, — RateLimit__RegisterPermitLimit
# (регистрация строже логина: она ещё и пишет в БД).
$env:RateLimit__RegisterPermitLimit = '5'
$env:RateLimit__AuthWindowSeconds = '60'

# Api лежит в backend/: путь считаем от корня репозитория ($root), а не от текущего каталога.
$apiProject = Join-Path $root 'backend/src/Pingboard.Api/Pingboard.Api.csproj'

# Собираем заранее и однопоточно: в песочнице параллельные узлы MSBuild не работают (-m:1).
Write-Host '== Сборка Api' -ForegroundColor Cyan
& dotnet build $apiProject -m:1 --nologo | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host 'FAIL: не удалось собрать Api' -ForegroundColor Red
    exit 1
}

Write-Host "== Запускаю Api на $baseUrl (логи: $logPath)" -ForegroundColor Cyan
$process = Start-Process -FilePath 'dotnet' `
    -ArgumentList 'run', '--project', $apiProject, '-m:1', '--no-launch-profile', '--no-build' `
    -WorkingDirectory $root -NoNewWindow -PassThru `
    -RedirectStandardOutput $logPath -RedirectStandardError "$logPath.err"

$failed = $false

<#
.SYNOPSIS
    Кодирует байты в base64url (без '=' и с '-'/'_' вместо '+'/'/').
#>
function ConvertTo-Base64Url {
    param([byte[]] $Bytes)

    return [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

<#
.SYNOPSIS
    Выписывает HS256-JWT с claim sub — ровно такой, какой выдаёт JwtTokenService.

.DESCRIPTION
    Секрет тот же, что у запущенного Api ($env:Jwt__Secret), поэтому токен должен
    приниматься. Отдельная проверка с чужим секретом подтверждает, что подпись
    действительно валидируется, а не игнорируется.
#>
function New-BearerToken {
    param(
        [string] $Secret,
        [string] $Subject,
        [string] $Issuer,
        [string] $Audience,
        [int] $LifetimeSeconds
    )

    # Время берём через DateTimeOffset: `Get-Date -Date` принимает .NET-такты, а не
    # юникс-секунды, поэтому «-Date '1970-01-01' -UFormat %s» даёт 0 — токен получался
    # с exp в районе 1970 года и отвергался как просроченный.
    $now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $header = '{"alg":"HS256","typ":"JWT"}'
    $payload = '{"sub":"' + $Subject + '","iss":"' + $Issuer + '","aud":"' + $Audience +
               '","exp":' + ($now + $LifetimeSeconds) + ',"nbf":' + ($now - 10) + '}'

    $encodedHeader = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes($header))
    $encodedPayload = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes($payload))
    $signingInput = $encodedHeader + '.' + $encodedPayload

    $hmac = New-Object System.Security.Cryptography.HMACSHA256
    $hmac.Key = [Text.Encoding]::UTF8.GetBytes($Secret)
    $signature = ConvertTo-Base64Url ($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($signingInput)))

    return $signingInput + '.' + $signature
}

<#
.SYNOPSIS
    Возвращает HTTP-статус запроса, не роняя скрипт на 4xx/5xx.
#>
function Get-HttpStatus {
    param(
        [string] $Method,
        [string] $Url,
        [string] $Token
    )

    $headers = @{}
    if (-not [string]::IsNullOrEmpty($Token)) { $headers['Authorization'] = 'Bearer ' + $Token }

    try {
        $response = Invoke-WebRequest -Uri $Url -Method $Method -Headers $headers -TimeoutSec 15 -UseBasicParsing
        return [int]$response.StatusCode
    }
    catch {
        if ($null -ne $_.Exception.Response) { return [int]$_.Exception.Response.StatusCode }
        throw
    }
}

<#
.SYNOPSIS
    Достаёт статус, Content-Type, WWW-Authenticate и тело даже у ответов 4xx/5xx.

.DESCRIPTION
    Invoke-WebRequest бросает исключение на любом коде >= 400, а нам нужны именно эти
    ответы: формат ошибки (RFC 7807) — часть контракта с фронтом, и проверять его надо
    на живом процессе, а не на догадках.
#>
function Get-ErrorResponse {
    param(
        [string] $Method,
        [string] $Url,
        [string] $Body,
        [string] $Token
    )

    $headers = @{}
    if (-not [string]::IsNullOrEmpty($Token)) { $headers['Authorization'] = 'Bearer ' + $Token }

    $request = @{ Uri = $Url; Method = $Method; Headers = $headers; TimeoutSec = 15; UseBasicParsing = $true }
    if (-not [string]::IsNullOrEmpty($Body)) {
        $request['Body'] = $Body
        $request['ContentType'] = 'application/json'
    }

    try {
        $response = Invoke-WebRequest @request

        return [pscustomobject]@{
            Status          = [int]$response.StatusCode
            ContentType     = [string]$response.Headers['Content-Type']
            WwwAuthenticate = [string]$response.Headers['WWW-Authenticate']
            RetryAfter      = [string]$response.Headers['Retry-After']
            Body            = [string]$response.Content
        }
    }
    catch {
        $web = $_.Exception.Response
        if ($null -eq $web) { throw }

        $contentType = ''
        $challenge = ''
        $retryAfter = ''
        $body = ''

        try { $contentType = [string]$web.Headers['Content-Type'] } catch { }
        try { $challenge = [string]$web.Headers['WWW-Authenticate'] } catch { }
        try { $retryAfter = [string]$web.Headers['Retry-After'] } catch { }

        try {
            $reader = New-Object System.IO.StreamReader($web.GetResponseStream())
            $body = $reader.ReadToEnd()
            $reader.Dispose()
        }
        catch { }

        return [pscustomobject]@{
            Status          = [int]$web.StatusCode
            ContentType     = $contentType
            WwwAuthenticate = $challenge
            RetryAfter      = $retryAfter
            Body            = $body
        }
    }
}

try {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $ready = $false

    while ((Get-Date) -lt $deadline) {
        if ($process.HasExited) { break }

        try {
            $health = Invoke-RestMethod -Uri "$baseUrl/healthz" -TimeoutSec 3
            if ($health.status -eq 'ok') { $ready = $true; break }
        }
        catch {
            Start-Sleep -Milliseconds 700
        }
    }

    if (-not $ready) {
        Write-Host 'FAIL: Api не ответил на /healthz' -ForegroundColor Red
        if (Test-Path $logPath) { Get-Content $logPath -Tail 30 }
        if (Test-Path "$logPath.err") { Get-Content "$logPath.err" -Tail 30 }
        $failed = $true
    }
    else {
        Write-Host 'OK:  /healthz -> 200 {status: ok}' -ForegroundColor Green
    }

    if (-not $failed) {
        try {
            $readyzResponse = Invoke-WebRequest -Uri "$baseUrl/readyz" -TimeoutSec 30 -UseBasicParsing
            Write-Host "WARN: /readyz ответил $($readyzResponse.StatusCode) — БД неожиданно доступна" -ForegroundColor Yellow
        }
        catch {
            $status = $_.Exception.Response.StatusCode.value__
            if ($status -eq 503) {
                Write-Host 'OK:  /readyz -> 503 (БД недоступна, процесс при этом жив)' -ForegroundColor Green
            }
            else {
                Write-Host "FAIL: /readyz вернул неожиданный код $status" -ForegroundColor Red
                $failed = $true
            }
        }
    }

    if (-not $failed) {
        Write-Host '== JWT: анонимный доступ запрещён, валидный токен принимается' -ForegroundColor Cyan
        try {
            $monitorUrl = "$baseUrl/api/monitors"
            $anonStatus = Get-HttpStatus -Method 'GET' -Url $monitorUrl -Token ''
            if ($anonStatus -eq 401) {
                Write-Host 'OK:  GET /api/monitors без токена -> 401' -ForegroundColor Green
            }
            else {
                Write-Host "FAIL: без токена ожидался 401, получен $anonStatus" -ForegroundColor Red
                $failed = $true
            }

            $validToken = New-BearerToken -Secret $env:Jwt__Secret `
                -Subject '33333333-3333-3333-3333-333333333333' -Issuer 'pingboard' -Audience 'pingboard' `
                -LifetimeSeconds 600
            $validStatus = Get-HttpStatus -Method 'GET' -Url $monitorUrl -Token $validToken
            if ($validStatus -eq 401) {
                Write-Host 'FAIL: валидный токен отвергнут (401) — выдача и проверка разошлись' -ForegroundColor Red
                $failed = $true
            }
            else {
                # 503 ожидаем: БД в этом режиме недоступна. Главное — не 401: запрос
                # прошёл аутентификацию и упал уже на обращении к Postgres.
                Write-Host "OK:  GET /api/monitors с валидным токеном -> $validStatus (401 исключён)" -ForegroundColor Green
            }

            $foreignToken = New-BearerToken -Secret 'a-completely-different-secret-value-0123456789' `
                -Subject '33333333-3333-3333-3333-333333333333' -Issuer 'pingboard' -Audience 'pingboard' `
                -LifetimeSeconds 600
            $foreignStatus = Get-HttpStatus -Method 'GET' -Url $monitorUrl -Token $foreignToken
            if ($foreignStatus -eq 401) {
                Write-Host 'OK:  токен с чужой подписью -> 401' -ForegroundColor Green
            }
            else {
                Write-Host "FAIL: токен с чужой подписью принят ($foreignStatus) — подпись не проверяется" -ForegroundColor Red
                $failed = $true
            }
        }
        catch {
            Write-Host "FAIL: проверка JWT не удалась: $($_.Exception.Message)" -ForegroundColor Red
            $failed = $true
        }
    }

    if (-not $failed) {
        Write-Host '== Ошибки: RFC 7807, realm в 401 и 400 на битый JSON' -ForegroundColor Cyan
        try {
            $anonymous = Get-ErrorResponse -Method 'GET' -Url "$baseUrl/api/monitors" -Token ''

            if ($anonymous.Status -eq 401) {
                Write-Host 'OK:  без токена 401' -ForegroundColor Green
            }
            else {
                Write-Host "FAIL: без токена ожидался 401, получен $($anonymous.Status)" -ForegroundColor Red
                $failed = $true
            }

            # RFC 7807 нарушается, если тело приходит как application/json: клиенты,
            # которые разбирают проблему по media type, перестают её видеть.
            if ($anonymous.ContentType -like 'application/problem+json*') {
                Write-Host "OK:  Content-Type ошибки — $($anonymous.ContentType)" -ForegroundColor Green
            }
            else {
                Write-Host "FAIL: Content-Type ошибки '$($anonymous.ContentType)', ожидался application/problem+json" -ForegroundColor Red
                $failed = $true
            }

            # README §4 обещает realm: голое «Bearer» часть клиентов считает невалидным челленджем.
            if ($anonymous.WwwAuthenticate -match 'realm="pingboard"') {
                Write-Host "OK:  WWW-Authenticate — $($anonymous.WwwAuthenticate)" -ForegroundColor Green
            }
            else {
                Write-Host "FAIL: WWW-Authenticate '$($anonymous.WwwAuthenticate)' без realm=`"pingboard`"" -ForegroundColor Red
                $failed = $true
            }

            if ($anonymous.Body -match 'traceId') {
                Write-Host 'OK:  в теле ошибки есть traceId' -ForegroundColor Green
            }
            else {
                Write-Host "FAIL: в теле ошибки нет traceId: $($anonymous.Body)" -ForegroundColor Red
                $failed = $true
            }

            # Битый JSON — ошибка клиента (400), а не «внутренняя ошибка сервиса» (500):
            # каждая опечатка клиента не должна выглядеть как авария сервиса.
            $broken = Get-ErrorResponse -Method 'POST' -Url "$baseUrl/api/auth/register" -Body '{"email":'

            if ($broken.Status -eq 400) {
                Write-Host 'OK:  POST /api/auth/register с битым JSON -> 400' -ForegroundColor Green
            }
            else {
                Write-Host "FAIL: битый JSON дал $($broken.Status), ожидался 400" -ForegroundColor Red
                $failed = $true
            }

            if ($broken.ContentType -like 'application/problem+json*') {
                Write-Host 'OK:  тело 400 размечено как application/problem+json' -ForegroundColor Green
            }
            else {
                Write-Host "FAIL: тело 400 размечено как '$($broken.ContentType)'" -ForegroundColor Red
                $failed = $true
            }

            # Rate limit: с RateLimit__RegisterPermitLimit=5 один из следующих запросов обязан получить 429.
            # Отказ отдаётся тем же форматом (ProblemDetails + traceId) и с заголовком Retry-After.
            $limited = $null
            for ($attempt = 1; $attempt -le 10 -and $null -eq $limited; $attempt++) {
                $probe = Get-ErrorResponse -Method 'POST' -Url "$baseUrl/api/auth/register" -Body '{"email":'
                if ($probe.Status -eq 429) { $limited = $probe }
            }

            if ($null -ne $limited) {
                Write-Host "OK:  лимит на /api/auth/* сработал (429, Retry-After: $($limited.RetryAfter))" -ForegroundColor Green

                if ($limited.ContentType -like 'application/problem+json*') {
                    Write-Host 'OK:  тело 429 размечено как application/problem+json' -ForegroundColor Green
                }
                else {
                    Write-Host "FAIL: тело 429 размечено как '$($limited.ContentType)'" -ForegroundColor Red
                    $failed = $true
                }
            }
            else {
                Write-Host 'FAIL: rate limit на /api/auth/* не сработал за 10 запросов' -ForegroundColor Red
                $failed = $true
            }
        }
        catch {
            Write-Host "FAIL: проверка формата ошибок не удалась: $($_.Exception.Message)" -ForegroundColor Red
            $failed = $true
        }
    }

    if (-not $failed) {
        try {
            $openapi = Invoke-RestMethod -Uri "$baseUrl/openapi/v1.json" -TimeoutSec 10
            $paths = $openapi.paths.PSObject.Properties.Name
            Write-Host "OK:  /openapi/v1.json содержит $($paths.Count) путей:" -ForegroundColor Green
            $paths | Sort-Object | ForEach-Object { Write-Host "       $_" }

            $expected = @(
                '/healthz', '/readyz',
                '/api/auth/register', '/api/auth/login',
                '/api/monitors', '/api/monitors/{id}', '/api/monitors/{id}/checks'
            )

            $missing = $expected | Where-Object { $paths -notcontains $_ }
            if ($missing) {
                Write-Host "FAIL: в OpenAPI нет путей: $($missing -join ', ')" -ForegroundColor Red
                $failed = $true
            }
            else {
                Write-Host 'OK:  все маршруты из §5 PLAN.md объявлены' -ForegroundColor Green
            }

            # Спецификация обязана отражать реальную защиту: иначе и Scalar, и клиенты
            # считают маршруты мониторов анонимными.
            $schemeNames = @($openapi.components.securitySchemes.PSObject.Properties.Name)
            $monitorSecurity = $openapi.paths.'/api/monitors'.get.security

            if (($schemeNames -contains 'Bearer') -and ($null -ne $monitorSecurity)) {
                Write-Host 'OK:  OpenAPI помечает /api/monitors как требующий Bearer-токен' -ForegroundColor Green
            }
            else {
                Write-Host "FAIL: OpenAPI не описывает Bearer-защиту (schemes: $($schemeNames -join ', '))" -ForegroundColor Red
                $failed = $true
            }
        }
        catch {
            Write-Host "FAIL: не удалось прочитать OpenAPI: $($_.Exception.Message)" -ForegroundColor Red
            $failed = $true
        }
    }
}
finally {
    if (-not $process.HasExited) {
        Write-Host '== Останавливаю Api' -ForegroundColor Cyan
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 1
    }

    if (Test-Path "$logPath.err") { Remove-Item "$logPath.err" -Force -ErrorAction SilentlyContinue }
}

if ($failed) { exit 1 }

Write-Host 'SMOKE OK' -ForegroundColor Green
exit 0
