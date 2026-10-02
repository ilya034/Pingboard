<#
.SYNOPSIS
    Единая точка входа для restore/build решения Pingboard.

.DESCRIPTION
    Обёртка над `dotnet`, которая сама подставляет `-m:1`, если команда выполняется
    внутри песочницы DSH (флаг `$env:DSH_SHELL`), и не мешает полноценной
    многоузловой сборке вне песочницы.

    Зачем это нужно. В песочнице DSH на Windows (workspace-write) команды запускаются
    от restricted-токена с пониженной целостностью. Такой токен не может открыть канал
    к процессу-узлу MSBuild, поэтому многоузловая сборка падает с «Ошибка сборки.
    Ошибок: 0» и exit code 1, а VSTest-хост падает на OpenProcess -> Access denied.
    Один узел (`-m:1`) проблем не создаёт, подробности — в SANDBOX.md.

.PARAMETER Target
    restore | build | clean   (по умолчанию build)

.PARAMETER Project
    Что собирать. По умолчанию решение backend/Pingboard.sln, можно передать путь к .csproj.

.EXAMPLE
    powershell -File scripts/build.ps1
    powershell -File scripts/build.ps1 -Target restore
    powershell -File scripts/build.ps1 -Project backend/src/Pingboard.Api/Pingboard.Api.csproj

.NOTES
    Проверить, какая ветка кода выбрана: скрипт печатает режим в первой строке.
#>
[CmdletBinding()]
param(
    [ValidateSet('restore', 'build', 'clean')]
    [string] $Target = 'build',

    [string] $Project = 'backend/Pingboard.sln',

    [switch] $NoIncremental
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# В песочнице DSH параллельные узлы MSBuild недоступны: каждый узел общается с родителем
# по именованному каналу, а restricted-токен не может его открыть. Один узел работает всегда.
$inSandbox = [bool]$env:DSH_SHELL
$nodeArgs = if ($inSandbox) { @('-m:1') } else { @() }

if ($inSandbox) {
    Write-Host 'Режим: песочница DSH -> сборка одним узлом MSBuild (-m:1).' -ForegroundColor Yellow
} else {
    Write-Host 'Режим: обычная машина -> многоузловая сборка MSBuild.' -ForegroundColor Green
}

if (-not (Test-Path $Project)) {
    throw "Не найден проект: $Project (текущий каталог: $root)"
}

$dotnetArgs = @($Target, $Project, '--nologo') + $nodeArgs
if ($Target -eq 'build') {
    # В песочнице -m:1 и без инкрементального ускорения Visual Studio: последнее живёт
    # в MSBuild-сервере, то есть в том же недоступном канале.
    if ($inSandbox) { $dotnetArgs += '-p:AccelerateBuildsInVisualStudio=false' }
    if ($NoIncremental) { $dotnetArgs += '--no-incremental' }
}

Write-Host ('dotnet ' + ($dotnetArgs -join ' ')) -ForegroundColor Cyan
# `| Out-Host` обязателен: без него stdout нативной команды не успевает слиться до выхода
# оболочки, и отчёт `dotnet` пропадает (код возврата при этом верный). См. SANDBOX.md.
& dotnet @dotnetArgs 2>&1 | Out-Host
exit $LASTEXITCODE
