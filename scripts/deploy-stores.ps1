param(
    [Parameter(Mandatory = $true)]
    [string]$PackageZip,

    [string]$StoresCsv = "deploy\stores.csv",

    [string]$DefaultInstallDir = "C:\SoundTransportation"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (!(Test-Path -LiteralPath $PackageZip)) {
    throw "Package not found: $PackageZip"
}

if (!(Test-Path -LiteralPath $StoresCsv)) {
    throw "Store list not found: $StoresCsv. Copy deploy\stores.example.csv to deploy\stores.csv first."
}

$stores = Import-Csv -LiteralPath $StoresCsv | Where-Object {
    $_.Enabled -eq $true -or $_.Enabled -eq "true" -or $_.Enabled -eq "1"
}

if ($stores.Count -eq 0) {
    throw "No enabled stores found in $StoresCsv"
}

$localInstaller = Join-Path $root "scripts\install-update-local.ps1"
if (!(Test-Path -LiteralPath $localInstaller)) {
    throw "Local installer script not found: $localInstaller"
}

foreach ($store in $stores) {
    $name = if ($store.Name) { $store.Name } else { $store.ComputerName }
    $computerName = $store.ComputerName
    $installDir = if ($store.InstallDir) { $store.InstallDir } else { $DefaultInstallDir }

    Write-Host "==== Deploying $name ($computerName) ===="
    $session = $null
    try {
        $session = New-PSSession -ComputerName $computerName
        $remoteRoot = "C:\Windows\Temp\SoundTransportationDeploy"
        $remoteZip = Join-Path $remoteRoot (Split-Path -Leaf $PackageZip)
        $remoteInstaller = Join-Path $remoteRoot "install-update-local.ps1"

        Invoke-Command -Session $session -ScriptBlock {
            param($Path)
            New-Item -ItemType Directory -Force $Path | Out-Null
        } -ArgumentList $remoteRoot

        Copy-Item -LiteralPath $PackageZip -Destination $remoteZip -ToSession $session -Force
        Copy-Item -LiteralPath $localInstaller -Destination $remoteInstaller -ToSession $session -Force

        Invoke-Command -Session $session -ScriptBlock {
            param($Script, $Zip, $InstallDir)
            powershell.exe -ExecutionPolicy Bypass -File $Script -PackageZip $Zip -InstallDir $InstallDir
        } -ArgumentList $remoteInstaller, $remoteZip, $installDir

        Write-Host "Deployment completed for $name."
    } catch {
        Write-Error "Deployment failed for $name ($computerName): $($_.Exception.Message)"
    } finally {
        if ($session) {
            Remove-PSSession $session
        }
    }
}
