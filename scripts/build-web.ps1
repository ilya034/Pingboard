# scripts/build-web.ps1 — сборка SPA: зависимости -> типы -> бандл.
#
# В песочнице DSH нужны два флага, и оба — из SANDBOX.md §8:
#   * --cache                 кэш npm по умолчанию лежит в %LOCALAPPDATA% (вне workspace) -> EPERM;
#   * --configLoader native   Vite на Windows зовёт exec("net use") -> spawn EPERM.
# На Linux и на обычной машине работает каноничный путь `npm ci && npm run build`
# (именно он зашит в deploy/Dockerfile.web).
#
# Готовые шаги лежат в frontend/package.json (build:sandbox), чтобы флаги не дублировались.

[CmdletBinding()]
param(
    [switch]$SkipInstall
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

    Write-Host '== tsc --noEmit && vite build --configLoader native' -ForegroundColor Cyan
    # `| Out-Host` обязателен: без него stdout нативной команды не успевает слиться до выхода
    # оболочки (SANDBOX.md §7).
    & npm run build:sandbox 2>&1 | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "сборка фронтенда не прошла (код $LASTEXITCODE)" }

    Write-Host '== готово: frontend/dist' -ForegroundColor Green
}
finally {
    Pop-Location
}
