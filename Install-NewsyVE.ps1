<#
  Instalator NewsyVE.

  Kopiuje NewsyVE.exe do %LOCALAPPDATA%\NewsyVE i tworzy skroty.
  Aplikacja jest jednym procesem - bez przegladarki i bez PowerShella w tle.

  Uzycie:
    .\Install-NewsyVE.ps1
    .\Install-NewsyVE.ps1 -Desktop -Autostart
#>
param(
    [switch]$Desktop,
    [switch]$Autostart,
    [switch]$NoLaunch,
    [switch]$Rebuild
)

$ErrorActionPreference = 'Stop'
$src = $PSScriptRoot
$dest = Join-Path $env:LOCALAPPDATA 'NewsyVE'
$exeSrc = Join-Path $src 'NewsyVE.exe'

# --- 1. kompilacja (jesli trzeba) -------------------------------------------
if ($Rebuild -or -not (Test-Path $exeSrc)) {
    & (Join-Path $src 'Build-NewsyVE.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Kompilacja nie powiodla sie.' }
}

# --- 2. zatrzymanie dzialajacej kopii ---------------------------------------
Get-Process NewsyVE -ErrorAction SilentlyContinue | ForEach-Object {
    $_.CloseMainWindow() | Out-Null
    Start-Sleep -Milliseconds 300
    if (-not $_.HasExited) { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
}
Start-Sleep -Milliseconds 600

# --- 3. pliki ---------------------------------------------------------------
if (-not (Test-Path $dest)) { New-Item -ItemType Directory -Path $dest | Out-Null }
Copy-Item $exeSrc (Join-Path $dest 'NewsyVE.exe') -Force
foreach ($f in 'NewsyVE.ico', 'README.md') {
    $p = Join-Path $src $f
    if (Test-Path $p) { Copy-Item $p $dest -Force }
}
# zrodla, zeby dalo sie przebudowac bez tego katalogu
$srcDir = Join-Path $dest 'src'
if (Test-Path (Join-Path $src 'src')) {
    if (-not (Test-Path $srcDir)) { New-Item -ItemType Directory -Path $srcDir | Out-Null }
    Copy-Item (Join-Path $src 'src\*.cs') $srcDir -Force
    Copy-Item (Join-Path $src 'Build-NewsyVE.ps1') $dest -Force
    if (Test-Path (Join-Path $src 'promo')) {
        Copy-Item (Join-Path $src 'promo') $dest -Recurse -Force
    }
}
Write-Host "Pliki: $dest" -ForegroundColor Green

# --- 4. skroty --------------------------------------------------------------
$shell = New-Object -ComObject WScript.Shell
function New-PVShortcut([string]$Path) {
    $dir = Split-Path $Path -Parent
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    $lnk = $shell.CreateShortcut($Path)
    $lnk.TargetPath = Join-Path $dest 'NewsyVE.exe'
    $lnk.WorkingDirectory = $dest
    $lnk.IconLocation = (Join-Path $dest 'NewsyVE.exe') + ',0'
    $lnk.Description = 'NewsyVE - pogoda, newsy i sport na pasku zadan'
    $lnk.WindowStyle = 1
    $lnk.Save()
    Write-Host "Skrot: $Path" -ForegroundColor Green
}

$programs = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
# porzadki po wersji opartej o przegladarke
# usuniecie poprzedniej nazwy aplikacji (PogodaVE) - proces, katalog, skroty
Get-Process PogodaVE -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
$oldDir = Join-Path $env:LOCALAPPDATA 'PogodaVE'
if (Test-Path $oldDir) { Remove-Item $oldDir -Recurse -Force -ErrorAction SilentlyContinue; Write-Host "Usunieto stary katalog: $oldDir" -ForegroundColor DarkGray }
foreach ($old in @('PogodaVE.lnk', 'PogodaVE (pasek zadan).lnk', 'PogodaVE (pasek zadań).lnk',
                   'NewsyVE (pasek zadan).lnk', 'NewsyVE (pasek zadań).lnk')) {
    $p = Join-Path $programs $old
    if (Test-Path $p) { Remove-Item $p -Force }
    $p = Join-Path $programs ('Startup\' + $old)
    if (Test-Path $p) { Remove-Item $p -Force }
}
$startMenu = Join-Path $programs 'NewsyVE.lnk'
New-PVShortcut $startMenu
$oldDesk = Join-Path ([Environment]::GetFolderPath('Desktop')) 'PogodaVE.lnk'
if (Test-Path $oldDesk) { Remove-Item $oldDesk -Force -ErrorAction SilentlyContinue }
if ($Desktop)   { New-PVShortcut (Join-Path ([Environment]::GetFolderPath('Desktop')) 'NewsyVE.lnk') }
if ($Autostart) { New-PVShortcut (Join-Path $programs 'Startup\NewsyVE.lnk') }

Write-Host ''
Write-Host 'Gotowe.' -ForegroundColor Cyan
Write-Host 'Menu Start -> "NewsyVE" -> prawy przycisk -> Przypnij do menu Start.'
if (-not $NoLaunch) { Start-Process (Join-Path $dest 'NewsyVE.exe') }
