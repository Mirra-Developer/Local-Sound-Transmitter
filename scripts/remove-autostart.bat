@echo off
setlocal

set "SHORTCUT_NAME=Sound Transportation.lnk"
set "REMOVED=0"

for %%D in (
  "%ProgramData%\Microsoft\Windows\Start Menu\Programs\Startup"
  "%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup"
) do (
  if exist "%%~D\%SHORTCUT_NAME%" (
    del /f /q "%%~D\%SHORTCUT_NAME%"
    set "REMOVED=1"
    echo Removed: %%~D\%SHORTCUT_NAME%
  )
)

if "%REMOVED%"=="0" echo No Sound Transportation startup shortcut was found.
echo.
echo Autostart removal complete. The program and configuration were not deleted.
pause
