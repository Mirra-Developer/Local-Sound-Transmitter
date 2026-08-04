@echo off
setlocal

set "REPO_URL=https://github.com/Mirra-Developer/Local-Sound-Transmitter.git"
set "PROJECT_DIR=%USERPROFILE%\Local-Sound-Transmitter"

echo ==========================================
echo Sound Transportation installer and runner
echo ==========================================
echo.

where git >nul 2>nul
if errorlevel 1 (
    echo [ERROR] Git was not found.
    echo Please install Git from:
    echo https://git-scm.com/downloads
    echo.
    pause
    exit /b 1
)

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] .NET SDK was not found.
    echo Please install .NET 8 SDK from:
    echo https://dotnet.microsoft.com/download
    echo.
    pause
    exit /b 1
)

if exist "%PROJECT_DIR%\.git" (
    echo Updating existing project:
    echo %PROJECT_DIR%
    cd /d "%PROJECT_DIR%"
    git pull
) else (
    echo Cloning project to:
    echo %PROJECT_DIR%
    git clone "%REPO_URL%" "%PROJECT_DIR%"
    if errorlevel 1 (
        echo [ERROR] Git clone failed.
        pause
        exit /b 1
    )
    cd /d "%PROJECT_DIR%"
)

echo.
echo Building project...
dotnet build
if errorlevel 1 (
    echo [ERROR] Build failed.
    pause
    exit /b 1
)

echo.
echo Starting Sound Transportation...
echo UI should open at http://127.0.0.1:5080
echo.
dotnet run --project src\SoundTransportation.Mixer --urls http://0.0.0.0:5080

pause
