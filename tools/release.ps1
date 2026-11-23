# Új plugin verzió kiadása a GitHubra.
#   powershell -ExecutionPolicy Bypass -File tools\release.ps1
# Kell hozzá: .NET SDK (vagy Visual Studio 2022), GitHub CLI (gh auth login), és a lib\ mappa (tools\copy-hdt-libs.ps1).

param(
    [switch]$Draft
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

# 1. Verzió az AssemblyInfo.cs-ből
$assemblyInfo = Get-Content "exportalas\Properties\AssemblyInfo.cs" -Raw -Encoding UTF8
if ($assemblyInfo -notmatch 'AssemblyVersion\("(\d+)\.(\d+)\.(\d+)') {
    throw "Nem találom az AssemblyVersion-t az AssemblyInfo.cs-ben."
}
$version = "$($Matches[1]).$($Matches[2]).$($Matches[3])"
$tag = "v$version"
Write-Host "Verzió: $tag"

# 2. Tiszta, felküldött állapotból adunk ki
if (git status --porcelain) {
    throw "Van nem commitolt változás. Előbb commitold és pushold."
}
git fetch --quiet
$branch = git rev-parse --abbrev-ref HEAD
if ((git rev-parse HEAD) -ne (git rev-parse "origin/$branch")) {
    throw "A(z) $branch ág nincs felküldve (git push)."
}
if (git tag --list $tag) {
    throw "A $tag tag már létezik. Emeld a verziót az AssemblyInfo.cs-ben."
}

# 3. CHANGELOG bejegyzés kiolvasása
$changelog = Get-Content "CHANGELOG.md" -Encoding UTF8
$start = ($changelog | Select-String -Pattern "^v$([regex]::Escape($version))\b" | Select-Object -First 1).LineNumber
if (-not $start) {
    throw "A CHANGELOG.md-ben nincs '$tag' kezdetű bejegyzés."
}
$notes = @()
for ($i = $start; $i -lt $changelog.Count; $i++) {
    if ($changelog[$i] -match '^v\d+\.\d+') { break }
    $notes += $changelog[$i]
}
$notesText = ($notes -join "`n").Trim()

# 4. Fordítás (Release módban a dist\exportalas.zip is elkészül)
dotnet build "exportalas\export.csproj" -c Release
if ($LASTEXITCODE -ne 0) { throw "A fordítás nem sikerült." }
$zip = Join-Path $root "dist\exportalas.zip"
if (-not (Test-Path $zip)) { throw "Nem készült el: $zip" }

# 5. GitHub Release
$ghArgs = @("release", "create", $tag, $zip, "--title", $tag, "--notes", $notesText, "--target", $branch)
if ($Draft) { $ghArgs += "--draft" }
gh @ghArgs
if ($LASTEXITCODE -ne 0) { throw "A GitHub Release létrehozása nem sikerült." }

Write-Host "Kész: $tag kiadva. A felhasználók pluginja 6 órán belül letölti."
