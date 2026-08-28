param(
    [string]$Version = (Get-Date -Format "yyyy.MM.dd.HHmm"),
    [string]$OutputRoot = "release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$packageName = "SoundTransportation-$Version"
$stageRoot = Join-Path $OutputRoot "stage"
$stageDir = Join-Path $stageRoot $packageName
$zipPath = Join-Path $OutputRoot "$packageName.zip"

if (Test-Path $stageDir) {
    Remove-Item $stageDir -Recurse -Force
}

New-Item -ItemType Directory -Force $stageDir | Out-Null

dotnet publish src\SoundTransportation.Mixer `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -o $stageDir

Set-Content -LiteralPath (Join-Path $stageDir "VERSION.txt") -Value $Version -Encoding UTF8
Copy-Item -LiteralPath (Join-Path $root "scripts\install-from-folder.ps1") -Destination (Join-Path $stageDir "install-from-folder.ps1") -Force
Copy-Item -LiteralPath (Join-Path $root "scripts\install-update-local.ps1") -Destination (Join-Path $stageDir "install-update-local.ps1") -Force
Copy-Item -LiteralPath (Join-Path $root "scripts\remove-autostart.bat") -Destination (Join-Path $stageDir "remove-autostart.bat") -Force

@"
@echo off
cd /d "%~dp0"
powershell.exe -ExecutionPolicy Bypass -File "%~dp0install-from-folder.ps1" -NoStart -NoStartup
pause
"@ | Set-Content -LiteralPath (Join-Path $stageDir "install.bat") -Encoding ASCII

@"
@echo off
cd /d "%~dp0"
start "" SoundTransportation.Mixer.exe
timeout /t 2 /nobreak >nul
start "" http://127.0.0.1:5080
"@ | Set-Content -LiteralPath (Join-Path $stageDir "start.bat") -Encoding ASCII

@"
@echo off
taskkill /IM SoundTransportation.Mixer.exe /F
"@ | Set-Content -LiteralPath (Join-Path $stageDir "stop.bat") -Encoding ASCII

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

New-Item -ItemType Directory -Force $OutputRoot | Out-Null
Compress-Archive -LiteralPath $stageDir -DestinationPath $zipPath -Force

Write-Host "Package created:"
Write-Host (Resolve-Path $zipPath)
