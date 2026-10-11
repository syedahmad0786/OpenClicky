# Build an unsigned MSIX from the published win-x64 EXE (Windows SDK required).
# Usage (Windows 11 Developer Mode recommended):
#   powershell -ExecutionPolicy Bypass -File scripts/publish-exe.ps1
#   powershell -ExecutionPolicy Bypass -File scripts/pack-msix.ps1
#
# Requires: makeappx.exe from Windows 10/11 SDK (PATH or auto-discovered under Windows Kits).

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$payload = Join-Path $root "artifacts\exe\win-x64"
$staging = Join-Path $root "artifacts\msix\staging"
$outDir = Join-Path $root "artifacts\msix"
$manifestSrc = Join-Path $root "packaging\msix\Package.appxmanifest"
$msix = Join-Path $outDir "OpenClicky.msix"

function Find-MakeAppx {
    $fromPath = Get-Command makeappx -ErrorAction SilentlyContinue
    if ($fromPath) {
        return $fromPath.Source
    }

    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    if (-not (Test-Path $kitsRoot)) {
        return $null
    }

    $candidate = Get-ChildItem -Path $kitsRoot -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending |
        ForEach-Object {
            $exe = Join-Path $_.FullName "x64\makeappx.exe"
            if (Test-Path $exe) { $exe }
        } |
        Select-Object -First 1

    return $candidate
}

if (-not (Test-Path (Join-Path $payload "OpenClicky.App.exe"))) {
    throw "Publish EXE first: scripts/publish-exe.ps1"
}

$makeappx = Find-MakeAppx
if (-not $makeappx) {
    throw "makeappx.exe not found. Install Windows SDK (App certification / packaging tools) or add makeappx to PATH."
}

Write-Host "Using makeappx: $makeappx"

New-Item -ItemType Directory -Force -Path $staging, $outDir | Out-Null
Get-ChildItem $staging -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item -Path (Join-Path $payload "*") -Destination $staging -Recurse -Force
Copy-Item -Path $manifestSrc -Destination (Join-Path $staging "AppxManifest.xml") -Force

New-Item -ItemType Directory -Force -Path (Join-Path $staging "Assets") | Out-Null
# Placeholder 1x1 PNGs so makeappx accepts the package; replace before Store submission.
$pngBytes = [Convert]::FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==")
foreach ($name in @("StoreLogo.png", "Square150x150Logo.png", "Square44x44Logo.png")) {
    [IO.File]::WriteAllBytes((Join-Path $staging "Assets\$name"), $pngBytes)
}

& $makeappx pack /d $staging /p $msix /o
if ($LASTEXITCODE -ne 0) {
    throw "makeappx failed with exit code $LASTEXITCODE"
}

Write-Host "Packed (unsigned): $msix"
Write-Host "Sign with signtool before sideload, or enable Developer Mode + Add-AppxPackage -AllowUnsigned if supported."
