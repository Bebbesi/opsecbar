<#
  Compila OpSecBar in versione Release nella cartella dist\OpSecBar.

  .\scripts\publish.ps1                  richiede .NET 8 Desktop Runtime sul PC di destinazione (cartella piccola)
  .\scripts\publish.ps1 -SelfContained   include il runtime .NET (funziona su qualsiasi Windows 10/11 x64)
#>
param([switch]$SelfContained)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "dist\OpSecBar"
$sc = $SelfContained.IsPresent.ToString().ToLower()

dotnet publish (Join-Path $root "src\OpSecBar\OpSecBar.csproj") -c Release -r win-x64 --self-contained $sc -o $out -nologo
if ($LASTEXITCODE -ne 0) { throw "Compilazione fallita." }

Write-Host ""
Write-Host "Pronto: $out\OpSecBar.exe" -ForegroundColor Green
Write-Host "Per installarlo per l'utente corrente: .\scripts\install.ps1"
