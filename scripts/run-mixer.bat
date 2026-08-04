@echo off
setlocal

cd /d "%~dp0\.."

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] .NET SDK was not found.
    echo Install .NET 8 SDK from:
    echo https://dotnet.microsoft.com/download
    pause
    exit /b 1
)

echo Starting Sound Transportation...
dotnet run --project src\SoundTransportation.Mixer --urls http://0.0.0.0:5080

pause
