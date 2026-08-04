param(
    [string]$Urls = "http://0.0.0.0:5080"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

dotnet run --project src\SoundTransportation.Mixer --urls $Urls
