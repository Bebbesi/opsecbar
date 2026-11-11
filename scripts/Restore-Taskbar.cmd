@echo off
rem Emergency: closes OpSec Bar and makes the original Windows taskbar visible again.
rem (Keyboard alternative: Ctrl + Alt + Shift + Q)
setlocal
set "EXE=%LOCALAPPDATA%\Programs\OpSecBar\OpSecBar.exe"
if not exist "%EXE%" set "EXE=%~dp0..\dist\OpSecBar\OpSecBar.exe"
if not exist "%EXE%" set "EXE=%~dp0..\src\OpSecBar\bin\Debug\net8.0-windows10.0.19041.0\OpSecBar.exe"

if exist "%EXE%" (
    "%EXE%" --restore
) else (
    echo OpSecBar.exe not found: ending the process, the watchdog will restore the taskbar.
)
taskkill /im OpSecBar.exe /f >nul 2>&1
echo Windows taskbar restored.
endlocal
