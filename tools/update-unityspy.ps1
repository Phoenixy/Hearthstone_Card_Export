# A Hearthstone memóriaolvasó (Firestone UnitySpy, MIT) forrásának frissítése az external\unityspy mappában.
#   powershell -ExecutionPolicy Bypass -File tools\update-unityspy.ps1            (a legújabb commit)
#   powershell -ExecutionPolicy Bypass -File tools\update-unityspy.ps1 -Ref <commit>
# Utána: commit, push, és a GitHub Actions lefordítja. Ha működik, emeld a verziót és add ki.

param(
    [string]$Ref = "master"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$target = Join-Path $root "external\unityspy"
$temp = Join-Path ([System.IO.Path]::GetTempPath()) ("unityspy-" + [guid]::NewGuid().ToString("N"))

git clone --quiet https://github.com/Zero-to-Heroes/unity-spy-.net4.5.git $temp
if ($LASTEXITCODE -ne 0) { throw "Nem sikerült letölteni a forrást." }
try {
    git -C $temp checkout --quiet $Ref
    if ($LASTEXITCODE -ne 0) { throw "Nincs ilyen commit/ág: $Ref" }
    $commit = (git -C $temp rev-parse HEAD).Trim()
    $date = (git -C $temp log -1 --format=%cs).Trim()

    foreach ($dir in "UnitySpy", "UnitySpy.HearthstoneLib") {
        Remove-Item (Join-Path $target $dir) -Recurse -Force -ErrorAction SilentlyContinue
        New-Item -ItemType Directory -Force -Path (Join-Path $target $dir) | Out-Null
        $src = Join-Path $temp $dir
        Get-ChildItem $src -Recurse -Filter *.cs | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } | ForEach-Object {
            $rel = $_.FullName.Substring($src.Length + 1)
            $dest = Join-Path (Join-Path $target $dir) $rel
            New-Item -ItemType Directory -Force -Path (Split-Path $dest) | Out-Null
            Copy-Item $_.FullName $dest
        }
    }
    Copy-Item (Join-Path $temp "LICENSE") (Join-Path $target "LICENSE") -Force

    $source = Join-Path $target "SOURCE.md"
    $text = Get-Content $source -Raw -Encoding UTF8
    $text = $text -replace '- Commit: .*', "- Commit: $commit ($date)"
    Set-Content $source $text -Encoding UTF8 -NoNewline
    Write-Host "Kész: $commit ($date)"
}
finally {
    Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
}
