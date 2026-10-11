# Build an unsigned MSIX from the published win-x64 EXE (Windows SDK required).
# Usage (Windows 11 Developer Mode recommended):
#   powershell -ExecutionPolicy Bypass -File scripts/publish-exe.ps1
#   powershell -ExecutionPolicy Bypass -File scripts/pack-msix.ps1
#
# Requires: makeappx.exe from Windows 10/11 SDK on PATH.

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$payload = Join-Path $root "artifacts\exe\win-x64"
$staging = Join-Path $root "artifacts\msix\staging"
$outDir = Join-Path $root "artifacts\msix"
$manifestSrc = Join-Path $root "packaging\msix\Package.appxmanifest"
$msix = Join-Path $outDir "OpenClicky.msix"

if (-not (Test-Path (Join-Path $payload "OpenClicky.App.exe"))) {
    throw "Publish EXE first: scripts/publish-exe.ps1"
}

if (-not (Get-Command makeappx -ErrorAction SilentlyContinue)) {
    throw "makeappx.exe not found. Install Windows SDK and add makeappx to PATH."
}

New-Item -ItemType Directory -Force -Path $staging, $outDir | Out-Null
Get-ChildItem $staging -Force | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item -Path (Join-Path $payload "*") -Destination $staging -Recurse -Force
Copy-Item -Path $manifestSrc -Destination (Join-Path $staging "AppxManifest.xml") -Force

New-Item -ItemType Directory -Force -Path (Join-Path $staging "Assets") | Out-Null
# Placeholder 1x1 PNGs so makeappx accepts the package; replace before Store submission.
$pngBytes = [Convert]::FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==")
foreach ($name in @("StoreLogo.png", "Square150x150Logo.png", "Square44x44Logo.png")) {
    [IO.File]::WriteAllBytes((Join-Path $staging "Assets\$name"), $pngBytes)
}

& makeappx pack /d $staging /p $msix /o
Write-Host "Packed (unsigned): $msix"
Write-Host "Sign with signtool before sideload, or enable Developer Mode + Add-AppxPackage -AllowUnsigned if supported."
