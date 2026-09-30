# Usuwa NewsyVE (proces, pliki, skroty).
$ErrorActionPreference = 'SilentlyContinue'

Get-Process NewsyVE | ForEach-Object { Stop-Process -Id $_.Id -Force }
Start-Sleep -Milliseconds 500

$programs = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
$targets = @(
    (Join-Path $programs 'NewsyVE.lnk'),
    (Join-Path $programs 'Startup\NewsyVE.lnk'),
    (Join-Path $programs 'NewsyVE (pasek zadan).lnk'),
    (Join-Path $programs 'NewsyVE (pasek zadań).lnk'),
    (Join-Path $programs 'Startup\NewsyVE (pasek zadań).lnk'),
    (Join-Path ([Environment]::GetFolderPath('Desktop')) 'NewsyVE.lnk')
)
foreach ($t in $targets) { if (Test-Path $t) { Remove-Item $t -Force; Write-Host "Usunieto skrot: $t" } }

$dest = Join-Path $env:LOCALAPPDATA 'NewsyVE'
if (Test-Path $dest) { Remove-Item $dest -Recurse -Force; Write-Host "Usunieto katalog: $dest" }

Write-Host 'NewsyVE odinstalowana.' -ForegroundColor Cyan
