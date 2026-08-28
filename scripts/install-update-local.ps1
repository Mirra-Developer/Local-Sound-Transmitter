param(
    [Parameter(Mandatory = $true)]
    [string]$PackageZip,

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

function Remove-StartupShortcut {
    $shortcutName = "Sound Transportation.lnk"
    @(
        (Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\Programs\Startup"),
        ([Environment]::GetFolderPath("Startup"))
    ) | ForEach-Object {
        $shortcutPath = Join-Path $_ $shortcutName
        if (Test-Path -LiteralPath $shortcutPath) {
            Remove-Item -LiteralPath $shortcutPath -Force
            Write-Host "Startup shortcut removed: $shortcutPath"
        }
    }
}

if (!(Test-Path -LiteralPath $PackageZip)) {
    throw "Package not found: $PackageZip"
}

$installDirFull = [System.IO.Path]::GetFullPath($InstallDir)
$backupConfig = Join-Path $env:TEMP "SoundTransportation-appsettings-backup.json"
$existingConfig = Join-Path $installDirFull "appsettings.json"
$hasExistingConfig = Test-Path -LiteralPath $existingConfig

Write-Host "Installing Sound Transportation to $installDirFull"

Get-Process SoundTransportation.Mixer -ErrorAction SilentlyContinue | ForEach-Object {
    try {
        Write-Host "Stopping running process $($_.Id) from $($_.Path)"
        Stop-Process -Id $_.Id -Force
    } catch {
        Write-Warning "Could not inspect or stop process $($_.Id): $($_.Exception.Message)"
    }
}

if ($hasExistingConfig -and !$OverwriteConfig) {
    Copy-Item -LiteralPath $existingConfig -Destination $backupConfig -Force
}

$tempRoot = Join-Path $env:TEMP ("SoundTransportation-update-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $tempRoot | Out-Null

try {
    Expand-Archive -LiteralPath $PackageZip -DestinationPath $tempRoot -Force
    $exe = Get-ChildItem -LiteralPath $tempRoot -Recurse -Filter "SoundTransportation.Mixer.exe" | Select-Object -First 1
    if ($null -eq $exe) {
        throw "Package does not contain SoundTransportation.Mixer.exe"
    }

    $packageDir = $exe.Directory.FullName
    New-Item -ItemType Directory -Force $installDirFull | Out-Null

    Get-ChildItem -LiteralPath $installDirFull -Force | Where-Object {
        $_.Name -notin @("appsettings.json", "logs")
    } | Remove-Item -Recurse -Force

    Copy-Item -Path (Join-Path $packageDir "*") -Destination $installDirFull -Recurse -Force

    if ($hasExistingConfig -and !$OverwriteConfig -and (Test-Path -LiteralPath $backupConfig)) {
        Copy-Item -LiteralPath $backupConfig -Destination $existingConfig -Force
        Write-Host "Existing appsettings.json preserved."
    }

    if (!$NoStart) {
        $installedExe = Join-Path $installDirFull "SoundTransportation.Mixer.exe"
        Start-Process -FilePath $installedExe -WorkingDirectory $installDirFull
        Write-Host "Started Sound Transportation."
    }

    if (!$NoStartup) {
        Set-StartupShortcut -InstallDir $installDirFull
    } else {
        Remove-StartupShortcut
    }

    Write-Host "Install/update complete."
} finally {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $backupConfig -Force -ErrorAction SilentlyContinue
}
