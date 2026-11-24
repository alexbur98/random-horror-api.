# Souls Companion: установка зависимостей, сборка, иконки и запуск — одной командой.
# Запуск из корня репозитория:  powershell -ExecutionPolicy Bypass -File .\Scripts\setup.ps1
# Ключи: -SkipIcons (не скачивать иконки), -NoRun (не запускать приложение после сборки).
param([switch]$SkipIcons, [switch]$NoRun)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)

function Step($text) { Write-Host "`n==> $text" -ForegroundColor Yellow }
function Refresh-Path {
    $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')
}
function Ensure($command, $wingetId, $check) {
    if (Get-Command $command -ErrorAction SilentlyContinue) {
        if (-not $check -or (& $check)) { return }
    }
    Step "Устанавливаю $wingetId"
    winget install --id $wingetId -e --accept-source-agreements --accept-package-agreements
    Refresh-Path
}

Step 'Проверяю .NET 8 SDK и Python'
Ensure 'dotnet' 'Microsoft.DotNet.SDK.8' { (dotnet --list-sdks 2>$null) -match '^8\.' }
Ensure 'python' 'Python.Python.3.12' { (python --version 2>&1) -match 'Python 3' }

Step 'Собираю приложение'
dotnet build -c Release
if ($LASTEXITCODE -ne 0) { throw 'Сборка не удалась — пришлите вывод выше.' }

Step 'Запускаю тесты'
dotnet test -c Release --no-build
if ($LASTEXITCODE -ne 0) { Write-Warning 'Часть тестов не прошла — пришлите вывод выше. Продолжаю.' }

if (-not $SkipIcons) {
    Step 'Ставлю Pillow (уменьшение иконок до 128×128)'
    python -m pip install --user --quiet pillow
    foreach ($game in 'ds1', 'ds2', 'ds3', 'sekiro', 'eldenring') {
        Step "Скачиваю иконки: $game (около 2 минут)"
        python Scripts/fetch_icons.py --game $game --wiki 2>&1 | Tee-Object -FilePath "Scripts\icons_$game.log"
    }
    Step 'Пересобираю, чтобы иконки попали рядом с exe'
    dotnet build -c Release
}

$exe = Get-ChildItem -Recurse -Filter DS1Companion.exe -Path src\DS1Companion.UI\bin\Release | Select-Object -First 1
Step "Готово: $($exe.FullName)"
if (-not $NoRun) { Start-Process $exe.FullName }
