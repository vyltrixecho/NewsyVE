# Buduje NewsyVE-Setup.exe - jeden plik z wbudowana aplikacja i README.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "Nie znaleziono csc.exe: $csc" }

# aplikacja musi byc aktualna
& (Join-Path $root 'Build-NewsyVE.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Kompilacja NewsyVE.exe nie powiodla sie.' }

$app = Join-Path $root 'NewsyVE.exe'
$readme = Join-Path $root 'README.md'
$ico = Join-Path $root 'NewsyVE.ico'
$out = Join-Path $root 'NewsyVE-Setup.exe'

# wersja instalatora = wersja aplikacji (jedno zrodlo: src\AssemblyInfo.cs)
$info = Get-Content (Join-Path $root 'src\AssemblyInfo.cs') -Raw
if ($info -notmatch 'AssemblyVersion\("([\d.]+)"\)') { throw 'Brak AssemblyVersion w src\AssemblyInfo.cs' }
$ver = $Matches[1]
$verCs = Join-Path ([IO.Path]::GetTempPath()) 'NewsyVE-SetupVersion.cs'
Set-Content $verCs -Encoding ASCII -Value @(
    'using System.Reflection;',
    "[assembly: AssemblyVersion(`"$ver`")]",
    "[assembly: AssemblyFileVersion(`"$ver`")]")

$args = @(
    '/nologo', '/target:winexe', '/optimize+', '/platform:anycpu',
    "/out:`"$out`"",
    '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll',
    "/resource:`"$app`",NewsyVE.exe"
)
if (Test-Path $readme) { $args += "/resource:`"$readme`",README.md" }
if (Test-Path $ico) { $args += "/win32icon:`"$ico`"" }
$args += "`"$(Join-Path $root 'setup\Setup.cs')`""
$args += "`"$verCs`""

$log = & $csc $args 2>&1
$code = $LASTEXITCODE
Remove-Item $verCs -ErrorAction SilentlyContinue
$log | Where-Object { $_ -match 'error|warning CS' } | Select-Object -First 20
if ($code -ne 0) { Write-Host "BLAD KOMPILACJI ($code)" -ForegroundColor Red; exit 1 }

$kb = [Math]::Round((Get-Item $out).Length / 1kb, 1)
Write-Host "OK: $out ($kb kB, wersja $ver)" -ForegroundColor Green
