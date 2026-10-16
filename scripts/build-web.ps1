# scripts/build-web.ps1 — проверки и сборка SPA: зависимости -> линтер -> типы -> тесты -> бандл.
#
# Флаги для выполнения в изолированном окружении:
#   * --cache                 кэш npm внутри рабочей директории проекта;
#   * --configLoader native   прямая загрузка конфигурации Vite без системных утилит;
#   * --pool=threads          поточный пул Vitest.
# Каноничный запуск для обычного окружения: `npm ci && npm run lint && npm test && npm run build`.
#
# Готовые сценарии определены в frontend/package.json.

[CmdletBinding()]
param(
    [switch]$SkipInstall,
    # Пропустить проверки и собрать только бандл (быстрый путь, когда линтер уже отработал).
    [switch]$SkipChecks
)

# ErrorActionPreference = 'Stop' здесь не годится: в PowerShell 5.1 любая строка, которую
# нативная команда пишет в stderr (а npm пишет туда свои notice), превращается в завершающую
# ошибку NativeCommandError. Коды возврата проверяем сами.
$ErrorActionPreference = 'Continue'

$root = Split-Path -Parent $PSScriptRoot
$frontend = Join-Path $root 'frontend'
$cache = Join-Path $root '.npm-cache'

Push-Location $frontend
try {
    if (-not $SkipInstall) {
        Write-Host '== npm install' -ForegroundColor Cyan
        & npm install --cache $cache --no-audit --no-fund 2>&1 | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "npm install завершился с кодом $LASTEXITCODE" }
    }

    if (-not $SkipChecks) {
        Write-Host '== eslint .' -ForegroundColor Cyan
        & npm run lint 2>&1 | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "линтер нашёл ошибки (код $LASTEXITCODE)" }

        Write-Host '== vitest run --configLoader native --pool=threads' -ForegroundColor Cyan
        & npm run test:sandbox 2>&1 | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "тесты фронтенда не прошли (код $LASTEXITCODE)" }
    }

    Write-Host '== tsc --noEmit && vite build --configLoader native' -ForegroundColor Cyan
    # `| Out-Host` используется для гарантированного сброса stdout нативной команды перед завершением.
    & npm run build:sandbox 2>&1 | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "сборка фронтенда не прошла (код $LASTEXITCODE)" }

    Write-Host '== готово: frontend/dist' -ForegroundColor Green
}
finally {
    Pop-Location
}
