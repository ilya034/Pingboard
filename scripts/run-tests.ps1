<#
.SYNOPSIS
    Собирает решение Pingboard и запускает его тесты.

.DESCRIPTION
    Порядок работы:

      1. Сборка решения (`scripts/build.ps1`). Код должен компилироваться: иначе и VSTest,
         и in-process раннер работают по устаревшим сборкам из bin, и ошибка компиляции
         выглядит как «ВСЕ ТЕСТЫ ПРОШЛИ». Сборка — гейт: её сбой завершает скрипт с кодом 1.
      2. Тесты. Обычный путь — `dotnet test`. Если VSTest не работает (в песочнице VSTest-хост
         падает на OpenProcess → Access denied, потому что песочнице запрещено следить
         за родительским процессом), скрипт переключается на in-process раннер
         scripts/TestRunner — он делает то же самое (находит [Fact]/[Theory] и вызывает их),
         но без VSTest.

    Отступление на in-process раннер делается только когда сбой пришёл от самого VSTest
    (в выводе есть падение testhost на OpenProcess). Настоящее падение тестов остаётся
    падением и повторным прогоном не маскируется.

    Сбой `dotnet test` доходит до вызывающего кода двумя способами, и обрабатывать нужно оба:
    ненулевым кодом возврата и термирующей ошибкой от stderr нативной команды
    (при $ErrorActionPreference='Stop') — см. Invoke-Native.

.PARAMETER Mode
    auto    (по умолчанию) dotnet test, при сбое самого VSTest — in-process раннер.
    reflect только in-process раннер.
    vstest  только dotnet test.

.EXAMPLE
    powershell -File scripts/run-tests.ps1
    powershell -File scripts/run-tests.ps1 -Mode reflect
#>
[CmdletBinding()]
param(
    [ValidateSet('auto', 'reflect', 'vstest')]
    [string] $Mode = 'auto'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# Параллельные узлы MSBuild в песочнице DSH недоступны: узел общается с родителем
# по именованному каналу, а restricted-токен не может его открыть (подробности — SANDBOX.md).
# Один узел работает всегда, вне песочницы он не нужен.
#
# [string[]] обязателен: без приведения `if` разворачивает одноэлементный массив в пайплайн,
# $nodeArgs становится строкой, и `@nodeArgs` сплатит её по символам (`- m : 1` → MSB1001).
[string[]] $nodeArgs = if ($env:DSH_SHELL) { @('-m:1') } else { @() }

<#
.SYNOPSIS
    Запускает нативную команду, выводит её вывод и возвращает код возврата.

.DESCRIPTION
    `| Out-Host` обязателен: без него stdout нативной команды не успевает слиться до выхода
    оболочки, и отчёт `dotnet` пропадает (код возврата при этом верный).

    При $ErrorActionPreference='Stop' запись нативной команды в stderr (упавший testhost,
    ошибки компиляции) превращается в термирующую ошибку и обрывает функцию до
    `return $LASTEXITCODE`. Поэтому ошибка гасится здесь и превращается в код возврата —
    её текст уже выведен выше через `Out-Host`.
#>
function Invoke-Native {
    param([scriptblock] $Command)

    try {
        & $Command 2>&1 | Out-Host
    }
    catch {
        Write-Host $_.Exception.Message -ForegroundColor DarkGray
    }

    if ($LASTEXITCODE -isnot [int]) { return 1 }
    return $LASTEXITCODE
}

<#
.SYNOPSIS
    Отличает сбой самого VSTest (его лечит in-process раннер) от настоящего падения тестов.
#>
function Test-VstestBlocked {
    param([string] $Output)

    if ([string]::IsNullOrEmpty($Output)) { return $false }

    # Падение VSTest-хоста: «Процесс testhost для источника ... завершился с ошибкой.
    # Unhandled exception. System.ComponentModel.Win32Exception (5) ...» из OpenProcess.
    # Маркеры намеренно только латинские: сообщение локализовано, а строки с кириллицей
    # в этом файле чувствительны к кодировке (скрипт обязан быть UTF-8 с BOM).
    $hostFailed = $Output -match 'testhost' -and $Output -match 'Win32Exception'
    $openProcess = $Output -match 'OpenProcess'

    return ($hostFailed -and $openProcess)
}

# Вывод `dotnet test` снимается через System.Diagnostics.Process, а не через конвейер:
# при $ErrorActionPreference='Stop' запись нативной команды в stderr бросает термирующую
# NativeCommandError, из-за которой ни присвоить вывод переменной, ни гарантированно дочитать
# его до конца нельзя (проверено). Process отдаёт stdout/stderr и код возврата раздельно.
function Invoke-TestsVsTest {
    Write-Host '== dotnet test backend/Pingboard.sln' -ForegroundColor Cyan

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = 'dotnet'
    $startInfo.Arguments = (@('test', 'backend/Pingboard.sln') + $nodeArgs + @('--nologo')) -join ' '
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $startInfo.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    # Рабочий каталог — корень репозитория: путь к решению указан от него (backend/…).

    $process = [System.Diagnostics.Process]::Start($startInfo)
    $stdOut = $process.StandardOutput.ReadToEnd()
    $stdErr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()

    $exitCode = $process.ExitCode
    $process.Dispose()

    # stderr выводим в поток ошибок скрипта: сообщения об упавшем testhost не теряются.
    Write-Host $stdOut
    if (-not [string]::IsNullOrWhiteSpace($stdErr)) { Write-Host $stdErr -ForegroundColor DarkGray }

    return [pscustomobject]@{
        ExitCode = $exitCode
        Output   = ($stdOut + [Environment]::NewLine + $stdErr)
    }
}

function Invoke-TestsInProcess {
    Write-Host '== in-process раннер (backend/scripts/TestRunner)' -ForegroundColor Cyan
    return (Invoke-Native { dotnet run --project backend/scripts/TestRunner/Pingboard.TestRunner.csproj @nodeArgs })
}

# --- Гейт: код должен компилироваться (см. §1 в DESCRIPTION) ---
Write-Host '== Сборка решения' -ForegroundColor Cyan
$buildExitCode = $null
try {
    & (Join-Path $PSScriptRoot 'build.ps1') -Target build 2>&1 | ForEach-Object { "$_" } | Out-Host
    $buildExitCode = $LASTEXITCODE
}
catch {
    Write-Host $_.Exception.Message -ForegroundColor DarkGray
    $buildExitCode = 1
}

if ($buildExitCode -ne 0) {
    Write-Host 'FAIL: решение не собирается — тесты не запускались.' -ForegroundColor Red
    exit 1
}

switch ($Mode) {
    'vstest' {
        $run = Invoke-TestsVsTest
        exit $run.ExitCode
    }

    'reflect' {
        exit (Invoke-TestsInProcess)
    }

    default {
        $run = Invoke-TestsVsTest

        if ($run.ExitCode -eq 0) { exit 0 }

        if (Test-VstestBlocked -Output $run.Output) {
            Write-Warning 'VSTest-хост упал на OpenProcess (в песочнице это ожидаемо) — переключаюсь на in-process раннер.'
            exit (Invoke-TestsInProcess)
        }

        Write-Host 'dotnet test завершился с ошибкой (это не сбой VSTest-хоста) — fallback не применяю.' -ForegroundColor Red
        exit $run.ExitCode
    }
}
