@echo off
rem Emergenza: chiude OpSecBar e rende di nuovo visibile la taskbar originale di Windows.
rem (In alternativa, da tastiera: Ctrl + Alt + Maiusc + Q)
setlocal
set "EXE=%LOCALAPPDATA%\Programs\OpSecBar\OpSecBar.exe"
if not exist "%EXE%" set "EXE=%~dp0..\dist\OpSecBar\OpSecBar.exe"
if not exist "%EXE%" set "EXE=%~dp0..\src\OpSecBar\bin\Debug\net8.0-windows10.0.19041.0\OpSecBar.exe"

if exist "%EXE%" (
    "%EXE%" --restore
) else (
    echo OpSecBar.exe non trovato: chiudo il processo, la taskbar verra' ripristinata dal watchdog.
)
taskkill /im OpSecBar.exe /f >nul 2>&1
echo Taskbar di Windows ripristinata.
endlocal
