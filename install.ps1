<#
.SYNOPSIS
    Pobiera i instaluje najnowsze wydanie NewsyVE.

.DESCRIPTION
    Jedna komenda w terminalu Windows:

        irm https://raw.githubusercontent.com/vyltrixecho/NewsyVE/main/install.ps1 | iex

    Skrypt pyta GitHuba o najnowsze wydanie, pobiera instalator, sprawdza jego sume
    kontrolna SHA-256 i uruchamia instalacje. Nie wymaga uprawnien administratora -
    NewsyVE instaluje sie w katalogu uzytkownika (%LOCALAPPDATA%\NewsyVE).

    Z parametrami (potok nie przekazuje argumentow, wiec przez scriptblock):

        & ([scriptblock]::Create((irm https://raw.githubusercontent.com/vyltrixecho/NewsyVE/main/install.ps1))) -Silent

.PARAMETER Silent
    Instalacja bez okna instalatora (skrot na pulpicie, start po instalacji).

.PARAMETER DownloadOnly
    Tylko pobiera i sprawdza plik, nie uruchamia instalacji. Zwraca sciezke do pliku.

.PARAMETER Version
    Konkretny tag wydania, np. 'v2.0.0'. Domyslnie najnowsze.
#>

[CmdletBinding()]
param(
    [switch]$Silent,
    [switch]$DownloadOnly,
    [string]$Version
)

$ErrorActionPreference = 'Stop'

$Repozytorium = 'vyltrixecho/NewsyVE'

# Windows PowerShell 5.1 domyslnie probuje starych protokolow i odbija sie od GitHuba.
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Krok { param([string]$Tekst) Write-Host "==> $Tekst" -ForegroundColor Cyan }
function Uwaga { param([string]$Tekst) Write-Host "    $Tekst" -ForegroundColor Yellow }
function Dobrze { param([string]$Tekst) Write-Host "    $Tekst" -ForegroundColor Green }

# ---- wydanie ---------------------------------------------------------------

$adres = if ($Version) {
    "https://api.github.com/repos/$Repozytorium/releases/tags/$Version"
} else {
    "https://api.github.com/repos/$Repozytorium/releases/latest"
}

Krok 'Pytam GitHuba o wydanie'
$wydanie = Invoke-RestMethod -Uri $adres -Headers @{ 'User-Agent' = 'NewsyVE-Installer' }
Dobrze "$($wydanie.name) ($($wydanie.tag_name))"

$instalator = $wydanie.assets | Where-Object { $_.name -like '*Setup.exe' } | Select-Object -First 1
if (-not $instalator) { throw "Wydanie $($wydanie.tag_name) nie ma zalacznika z instalatorem." }

# ---- pobieranie ------------------------------------------------------------

$katalog = Join-Path ([IO.Path]::GetTempPath()) "NewsyVE-$($wydanie.tag_name)"
New-Item -ItemType Directory -Force -Path $katalog | Out-Null
$plik = Join-Path $katalog $instalator.name

Krok "Pobieram $($instalator.name) ($([math]::Round($instalator.size / 1KB)) kB)"
$postep = $ProgressPreference
$ProgressPreference = 'SilentlyContinue'   # Pasek postepu potrafi spowolnic pobieranie kilkukrotnie.
try {
    Invoke-WebRequest -Uri $instalator.browser_download_url -OutFile $plik -UseBasicParsing
}
finally {
    $ProgressPreference = $postep
}

# ---- suma kontrolna --------------------------------------------------------

$sumaAsset = $wydanie.assets | Where-Object { $_.name -like '*.sha256' } | Select-Object -First 1

if ($sumaAsset) {
    Krok 'Sprawdzam sume kontrolna'

    # GitHub oddaje zalaczniki jako application/octet-stream, wiec w Windows PowerShell
    # .Content jest tablica bajtow, a nie tekstem.
    $odpowiedz = Invoke-WebRequest -Uri $sumaAsset.browser_download_url -UseBasicParsing
    $tresc = if ($odpowiedz.Content -is [byte[]]) {
        [Text.Encoding]::ASCII.GetString($odpowiedz.Content)
    } else {
        [string]$odpowiedz.Content
    }

    $oczekiwana = ($tresc -split '\s+')[0].Trim().ToLower()
    $rzeczywista = (Get-FileHash $plik -Algorithm SHA256).Hash.ToLower()

    if ($oczekiwana -ne $rzeczywista) {
        Remove-Item $plik -Force -ErrorAction SilentlyContinue
        throw "Suma kontrolna sie nie zgadza.`n  oczekiwano: $oczekiwana`n  otrzymano:  $rzeczywista"
    }

    Dobrze "SHA-256 zgodna: $rzeczywista"
}
else {
    Uwaga 'Wydanie nie ma pliku .sha256 - pomijam weryfikacje.'
}

if ($DownloadOnly) {
    Krok 'Gotowe (tylko pobranie)'
    return $plik
}

# ---- instalacja ------------------------------------------------------------

Krok 'Uruchamiam instalator'
$argumenty = if ($Silent) { @('/silent') } else { @() }
# Bez -Wait: w Windows PowerShell 5.1 czeka ono takze na procesy potomne, czyli
# na uruchomione przez instalator NewsyVE - skrypt wisialby az do jego zamkniecia.
$proces = if ($argumenty.Count -gt 0) {
    Start-Process -FilePath $plik -ArgumentList $argumenty -PassThru
} else {
    Start-Process -FilePath $plik -PassThru
}
$proces.WaitForExit()

if ($proces.ExitCode -ne 0) {
    throw "Instalator zakonczyl sie kodem $($proces.ExitCode)."
}

Remove-Item $katalog -Recurse -Force -ErrorAction SilentlyContinue

$zainstalowany = Join-Path $env:LOCALAPPDATA 'NewsyVE\NewsyVE.exe'
if (Test-Path $zainstalowany) {
    Dobrze "Zainstalowano: $zainstalowany"
    Dobrze "Wersja: $((Get-Item $zainstalowany).VersionInfo.FileVersion)"
}
elseif ($Silent) {
    Uwaga 'Instalator zakonczyl sie bez bledu, ale nie widze pliku w domyslnym miejscu.'
}

Krok 'Gotowe'
