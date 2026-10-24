# Build an unsigned MSIX from the published win-x64 EXE (Windows SDK required).
# Usage (Windows 11 Developer Mode recommended):
#   powershell -ExecutionPolicy Bypass -File scripts/publish-exe.ps1
#   powershell -ExecutionPolicy Bypass -File scripts/pack-msix.ps1
#
# Requires: makeappx.exe from Windows 10/11 SDK (PATH or auto-discovered under Windows Kits).

param([string]$PayloadDirectory, [string]$OutputDirectory)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

$payload = if ($PayloadDirectory) { [IO.Path]::GetFullPath($PayloadDirectory) } else { Join-Path $root "artifacts\phase-one-preview\win-x64" }
$outDir = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $root "artifacts\phase-one-preview\msix" }
$staging = Join-Path $outDir "staging"
$manifestSrc = Join-Path $root "packaging\msix\Package.appxmanifest"
$msix = Join-Path $outDir "OpenClicky-0.6.0-unsigned.msix"
$resolvedStaging = [IO.Path]::GetFullPath($staging)
$expectedRoot = [IO.Path]::GetFullPath((Join-Path $root 'artifacts')) + [IO.Path]::DirectorySeparatorChar
if (-not $resolvedStaging.StartsWith($expectedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Package staging must remain under repository artifacts.' }

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
Get-ChildItem -LiteralPath $resolvedStaging -Force | ForEach-Object {
    if (-not $_.FullName.StartsWith($resolvedStaging + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe staging item.' }
    Remove-Item -LiteralPath $_.FullName -Recurse -Force
}
Copy-Item -Path (Join-Path $payload "*") -Destination $staging -Recurse -Force
Copy-Item -Path $manifestSrc -Destination (Join-Path $staging "AppxManifest.xml") -Force

New-Item -ItemType Directory -Force -Path (Join-Path $staging "Assets") | Out-Null
foreach ($name in @("StoreLogo.png", "Square150x150Logo.png", "Square44x44Logo.png")) {
    Copy-Item -LiteralPath (Join-Path $root "packaging\msix\Assets\$name") -Destination (Join-Path $staging "Assets\$name") -Force
}

& $makeappx pack /d $staging /p $msix /o
if ($LASTEXITCODE -ne 0) {
    throw "makeappx failed with exit code $LASTEXITCODE"
}

Write-Host "Packed (unsigned): $msix"
Write-Host "Sign with signtool before sideload, or enable Developer Mode + Add-AppxPackage -AllowUnsigned if supported."
