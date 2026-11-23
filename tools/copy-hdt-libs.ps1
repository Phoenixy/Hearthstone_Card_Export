# A géphez telepített Hearthstone Deck Tracker DLL-jeit a lib\ mappába másolja,
# hogy a plugin le tudjon fordulni. Futtatás a repó gyökeréből:
#   powershell -ExecutionPolicy Bypass -File tools\copy-hdt-libs.ps1
# Ha a HDT máshol van:
#   powershell -ExecutionPolicy Bypass -File tools\copy-hdt-libs.ps1 -HdtDir "D:\HDT"

param(
    [string]$HdtDir = ""
)

$ErrorActionPreference = "Stop"

if ($HdtDir -eq "") {
    $root = Join-Path $env:LOCALAPPDATA "HearthstoneDeckTracker"
    if (-not (Test-Path $root)) {
        throw "Nem találom a HDT-t itt: $root. Add meg kézzel: -HdtDir <mappa>"
    }
    # A HDT (Squirrel) app-<verzió> mappákba települ; a legújabbat választjuk.
    $latest = Get-ChildItem $root -Directory -Filter "app-*" |
        Sort-Object { [version]($_.Name -replace '^app-', '' -replace '-.*$', '') } -Descending |
        Select-Object -First 1
    if ($null -eq $latest) {
        throw "Nincs app-* mappa itt: $root"
    }
    $HdtDir = $latest.FullName
}

$files = @(
    "HearthstoneDeckTracker.exe",
    "HearthDb.dll",
    "HearthMirror.dll",
    "MahApps.Metro.dll",
    "Newtonsoft.Json.dll"
)

$lib = Join-Path (Split-Path $PSScriptRoot -Parent) "lib"
New-Item -ItemType Directory -Force -Path $lib | Out-Null

foreach ($f in $files) {
    $src = Join-Path $HdtDir $f
    if (-not (Test-Path $src)) {
        throw "Hiányzik: $src"
    }
    Copy-Item $src $lib -Force
    Write-Host "Másolva: $f"
}

Write-Host "Kész. HDT forrás: $HdtDir"
