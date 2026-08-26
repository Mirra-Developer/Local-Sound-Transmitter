param(
    [string]$SourceDir = $PSScriptRoot,
    [string]$InstallDir = "C:\SoundTransportation",
    [switch]$NoStart,
    [switch]$NoStartup,
    [switch]$OverwriteConfig
)

$ErrorActionPreference = "Stop"

function New-Shortcut {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ShortcutPath,

        [Parameter(Mandatory = $true)]
        [string]$TargetPath,

        [Parameter(Mandatory = $true)]
        [string]$WorkingDirectory
    )

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $TargetPath
    $shortcut.WorkingDirectory = $WorkingDirectory
    $shortcut.IconLocation = $TargetPath
    $shortcut.Save()
}

function Set-StartupShortcut {
    param(
        [Parameter(Mandatory = $true)]
        [string]$InstallDir
    )

    $exePath = Join-Path $InstallDir "SoundTransportation.Mixer.exe"
    if (!(Test-Path -LiteralPath $exePath)) {
        throw "Installed exe not found: $exePath"
    }

    $startupDir = Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\Programs\Startup"
    try {
        New-Item -ItemType Directory -Force $startupDir | Out-Null
        $shortcutPath = Join-Path $startupDir "Sound Transportation.lnk"
        New-Shortcut -ShortcutPath $shortcutPath -TargetPath $exePath -WorkingDirectory $InstallDir
        Write-Host "Startup shortcut created: $shortcutPath"
    } catch {
        $userStartupDir = [Environment]::GetFolderPath("Startup")
        New-Item -ItemType Directory -Force $userStartupDir | Out-Null
        $shortcutPath = Join-Path $userStartupDir "Sound Transportation.lnk"
        New-Shortcut -ShortcutPath $shortcutPath -TargetPath $exePath -WorkingDirectory $InstallDir
        Write-Host "User startup shortcut created: $shortcutPath"
    }
}

$sourceDirFull = [System.IO.Path]::GetFullPath($SourceDir)
$installDirFull = [System.IO.Path]::GetFullPath($InstallDir)

if (!(Test-Path -LiteralPath (Join-Path $sourceDirFull "SoundTransportation.Mixer.exe"))) {
    throw "Source folder does not contain SoundTransportation.Mixer.exe: $sourceDirFull"
}

$backupConfig = Join-Path $env:TEMP "SoundTransportation-appsettings-backup.json"
$existingConfig = Join-Path $installDirFull "appsettings.json"
$hasExistingConfig = Test-Path -LiteralPath $existingConfig

Write-Host "Installing Sound Transportation from $sourceDirFull to $installDirFull"

Get-Process SoundTransportation.Mixer -ErrorAction SilentlyContinue | ForEach-Object {
    try {
        if ($_.Path -and $_.Path.StartsWith($installDirFull, [System.StringComparison]::OrdinalIgnoreCase)) {
            Write-Host "Stopping running process $($_.Id)"
            Stop-Process -Id $_.Id -Force
        }
    } catch {
        Write-Warning "Could not inspect or stop process $($_.Id): $($_.Exception.Message)"
    }
}

if ($hasExistingConfig -and !$OverwriteConfig) {
    Copy-Item -LiteralPath $existingConfig -Destination $backupConfig -Force
}

New-Item -ItemType Directory -Force $installDirFull | Out-Null
Get-ChildItem -LiteralPath $installDirFull -Force | Where-Object {
    $_.Name -notin @("appsettings.json", "logs")
} | Remove-Item -Recurse -Force

Copy-Item -Path (Join-Path $sourceDirFull "*") -Destination $installDirFull -Recurse -Force

if ($hasExistingConfig -and !$OverwriteConfig -and (Test-Path -LiteralPath $backupConfig)) {
    Copy-Item -LiteralPath $backupConfig -Destination $existingConfig -Force
    Write-Host "Existing appsettings.json preserved."
}

if (!$NoStartup) {
    Set-StartupShortcut -InstallDir $installDirFull
}

if (!$NoStart) {
    $installedExe = Join-Path $installDirFull "SoundTransportation.Mixer.exe"
    Start-Process -FilePath $installedExe -WorkingDirectory $installDirFull
    Write-Host "Started Sound Transportation."
}

Remove-Item -LiteralPath $backupConfig -Force -ErrorAction SilentlyContinue
Write-Host "Install/update complete."
