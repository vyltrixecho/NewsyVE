# Przygotowuje pliki do GitHub Releases: dist\NewsyVE-Setup.exe i jego sume SHA-256.
# Nazwa instalatora jest stala (bez numeru wersji), wiec link
# .../releases/latest/download/NewsyVE-Setup.exe zawsze wskazuje najnowsze wydanie.
#
#   .\Build-Release.ps1            buduje i pokazuje polecenie gh do publikacji
#   .\Build-Release.ps1 -Publish   od razu tworzy wydanie vX.Y.Z na GitHubie (wymaga gh)
param([switch]$Publish)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

& (Join-Path $root 'Build-Setup.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Budowanie instalatora nie powiodlo sie.' }

$info = Get-Content (Join-Path $root 'src\AssemblyInfo.cs') -Raw
if ($info -notmatch 'AssemblyVersion\("(\d+)\.(\d+)\.(\d+)') { throw 'Brak AssemblyVersion w src\AssemblyInfo.cs' }
$tag = "v$($Matches[1]).$($Matches[2]).$($Matches[3])"

$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$setup = Join-Path $dist 'NewsyVE-Setup.exe'
Copy-Item (Join-Path $root 'NewsyVE-Setup.exe') $setup -Force

# format "suma  nazwa" - ten sam co sha256sum, czyta go install.ps1
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLower()
$sum = "$setup.sha256"
[IO.File]::WriteAllText($sum, "$hash  NewsyVE-Setup.exe`n", (New-Object Text.ASCIIEncoding))

Write-Host "Wydanie $tag" -ForegroundColor Cyan
Write-Host "  $setup"
Write-Host "  $sum"
Write-Host "  SHA-256: $hash"

$cmd = "gh release create $tag `"$setup`" `"$sum`" --title `"NewsyVE $($tag.Substring(1))`" --generate-notes"
if ($Publish) {
    Push-Location $root
    try { Invoke-Expression $cmd } finally { Pop-Location }
}
else {
    Write-Host ''
    Write-Host 'Publikacja (z katalogu repozytorium, po git push):' -ForegroundColor Cyan
    Write-Host "  $cmd"
}
