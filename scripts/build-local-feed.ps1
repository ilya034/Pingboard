<#
.SYNOPSIS
    Наполняет локальный оффлайн-фид .packages из кэша пакетов NuGet.

.DESCRIPTION
    В NuGet.config источниками объявлены только .packages (nuget.org недоступен в этой
    среде). Фид наполняется копированием .nupkg из %USERPROFILE%\.nuget\packages.
    Папка .packages в git не хранится — на новой машине команду запускают ещё раз.

.EXAMPLE
    powershell -File scripts/build-local-feed.ps1
#>
[CmdletBinding()]
param(
    [string] $Source = (Join-Path $env:USERPROFILE '.nuget\packages'),
    [string] $Target = (Join-Path (Split-Path -Parent $PSScriptRoot) '.packages')
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $Source)) {
    throw "Не найден кэш пакетов: $Source"
}

New-Item -ItemType Directory -Force -Path $Target | Out-Null

$packages = Get-ChildItem $Source -Recurse -Filter *.nupkg -File
Write-Host "Найдено пакетов: $($packages.Count). Копирую в $Target"

$packages | Copy-Item -Destination $Target -Force

$copied = (Get-ChildItem $Target -Filter *.nupkg -File).Count
Write-Host "Готово: в фиде $copied пакетов." -ForegroundColor Green
