# Kompiluje NewsyVE.exe kompilatorem C# z .NET Framework (zawsze jest w Windows).
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "Nie znaleziono csc.exe: $csc" }

$src = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { '"' + $_.FullName + '"' }
$out = Join-Path $root 'NewsyVE.exe'
$ico = Join-Path $root 'NewsyVE.ico'

$refs = @(
    '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll',
    '/r:System.Windows.Forms.dll', '/r:System.Web.Extensions.dll'
)

# WIC (przez WPF) rozpakowuje WebP, ktorego GDI+ nie zna. csc z katalogu
# Framework nie widzi tych bibliotek po samej nazwie - podajemy pelne sciezki.
$wpf = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\WPF'
foreach ($dll in 'PresentationCore.dll', 'WindowsBase.dll', '..\System.Xaml.dll') {
    $path = Join-Path $wpf $dll
    if (Test-Path $path) { $refs += "/r:`"$path`"" }
    else { Write-Warning "Brak $dll - miniatury WebP beda pomijane." }
}
$args = @('/nologo', '/target:winexe', '/optimize+', '/platform:anycpu',
          "/out:`"$out`"") + $refs
if (Test-Path $ico) { $args += "/win32icon:`"$ico`"" }
# reklamy Vyltrix Echo (wspolny zestaw _Wspolne\VyltrixPromo) - grafiki w zasobach exe,
# zeby okno ustawien wygladalo tak samo offline
foreach ($f in 'VyltrixEcho.png', 'BuyCoffeeQr.png', 'BuyCoffee.png') {
    $p = Join-Path $root "promo\$f"
    if (Test-Path $p) { $args += "/resource:`"$p`",VyltrixPromo.$f" }
    else { Write-Warning "Brak promo\$f - reklama w ustawieniach bedzie bez tej grafiki." }
}
$args += $src

$log = & $csc $args 2>&1
$code = $LASTEXITCODE
$log | Where-Object { $_ -match 'error|warning CS' } | Select-Object -First 25
if ($code -ne 0) { Write-Host "BLAD KOMPILACJI ($code)" -ForegroundColor Red; exit 1 }
Write-Host "OK: $out ($([Math]::Round((Get-Item $out).Length/1kb,1)) kB)" -ForegroundColor Green
