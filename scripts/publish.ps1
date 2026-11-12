<#
  Compila OpSecBar in versione Release nella cartella dist\OpSecBar.

  .\scripts\publish.ps1                  richiede .NET 8 Desktop Runtime sul PC di destinazione (cartella piccola)
  .\scripts\publish.ps1 -SelfContained   include il runtime .NET (funziona su qualsiasi Windows 10/11 x64)
  .\scripts\publish.ps1 -Build b20261112 build di prova: il tag finisce nella versione (es. 1.1.3-b20261112)
#>
param([switch]$SelfContained, [string]$Build)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "dist\OpSecBar"
$sc = $SelfContained.IsPresent.ToString().ToLower()

$extra = @()
if ($Build) {
    if ($Build -notmatch '^b\d{8}(-\d+)?$') { throw "Il tag della build deve essere tipo b20261112 o b20261112-2." }
    $version = ([xml](Get-Content (Join-Path $root "src\OpSecBar\OpSecBar.csproj"))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    $extra += "-p:InformationalVersion=$version-$Build"
}

dotnet publish (Join-Path $root "src\OpSecBar\OpSecBar.csproj") -c Release -r win-x64 --self-contained $sc -o $out -nologo @extra
if ($LASTEXITCODE -ne 0) { throw "Compilazione fallita." }

Write-Host ""
Write-Host "Pronto: $out\OpSecBar.exe" -ForegroundColor Green
Write-Host "Per installarlo per l'utente corrente: .\scripts\install.ps1"
