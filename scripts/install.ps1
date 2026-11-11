<#
  Installa OpSecBar per l'utente corrente (nessun privilegio di amministratore):
  copia dist\OpSecBar in %LocalAppData%\Programs\OpSecBar, crea il collegamento nel menu Start e lo avvia.
  L'avvio automatico con Windows si attiva da OpSecBar: Impostazioni > Sistema.

  .\scripts\install.ps1              installa (compila prima, se serve)
  .\scripts\install.ps1 -Uninstall   chiude OpSecBar, ripristina la taskbar di Windows e rimuove i file
#>
param([switch]$Uninstall)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root "dist\OpSecBar"
$target = Join-Path $env:LOCALAPPDATA "Programs\OpSecBar"
$exe = Join-Path $target "OpSecBar.exe"
$shortcut = Join-Path ([Environment]::GetFolderPath("Programs")) "OpSec Bar.lnk"

function Stop-OpSecBar {
    $running = Get-Process OpSecBar -ErrorAction SilentlyContinue
    if (-not $running) { return }
    $anyExe = ($running | Where-Object { $_.Path } | Select-Object -First 1).Path
    if ($anyExe) { Start-Process $anyExe -ArgumentList "--restore" -Wait }   # uscita pulita + ripristino taskbar
    Start-Sleep -Milliseconds 800
    Get-Process OpSecBar -ErrorAction SilentlyContinue | Stop-Process -Force
}

Stop-OpSecBar

if ($Uninstall) {
    Remove-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "OpSecBar" -ErrorAction SilentlyContinue
    Remove-Item $shortcut -ErrorAction SilentlyContinue
    Remove-Item $target -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "OpSecBar rimosso. Impostazioni e temi restano in $env:APPDATA\OpSecBar (eliminali a mano se non servono)."
    return
}

if (-not (Test-Path (Join-Path $source "OpSecBar.exe"))) { & (Join-Path $PSScriptRoot "publish.ps1") }

New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item (Join-Path $source "*") $target -Recurse -Force

$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($shortcut)
$link.TargetPath = $exe
$link.WorkingDirectory = $target
$link.Description = "OpSec Bar - alternative taskbar for Windows"
$link.Save()

Start-Process $exe
Write-Host "OpSecBar installato in $target e avviato." -ForegroundColor Green
