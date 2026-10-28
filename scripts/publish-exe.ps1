# Publish a self-contained win-x64 EXE for OpenClicky (unsigned).
# Run from repo root on Windows with .NET 8 SDK:
#   powershell -ExecutionPolicy Bypass -File scripts/publish-exe.ps1
param([string]$OpenWorkRuntimeDirectory, [string]$OutputDirectory)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not $OpenWorkRuntimeDirectory) { $OpenWorkRuntimeDirectory = Join-Path $root 'artifacts/runtime/openwork' }
& (Join-Path $PSScriptRoot 'setup-openwork.ps1') -TargetRoot $OpenWorkRuntimeDirectory -VerifyOnly
$out = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $root "artifacts/phase-one-preview/win-x64" }
New-Item -ItemType Directory -Force -Path $out | Out-Null

dotnet publish `
  (Join-Path $root "apps/windows/OpenClicky.App/OpenClicky.App.csproj") `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:IncludeAllContentForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -o $out

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$exe = Join-Path $out "OpenClicky.App.exe"
if (-not (Test-Path $exe)) {
    throw "Expected publish output missing: $exe"
}

# Friendly alias for manual installs (MSIX still launches OpenClicky.App.exe).
Copy-Item -Path $exe -Destination (Join-Path $out "OpenClicky.exe") -Force
foreach ($notice in @("LICENSE", "THIRD-PARTY-NOTICES.md")) {
    Copy-Item -LiteralPath (Join-Path $root $notice) -Destination (Join-Path $out $notice) -Force
}
Copy-Item -LiteralPath (Join-Path $root "docs/portable-quick-start.md") -Destination (Join-Path $out "README.md") -Force
$openWorkOut = Join-Path $out 'runtime/openwork'
New-Item -ItemType Directory -Force -Path (Join-Path $openWorkOut 'engine') | Out-Null
Copy-Item -LiteralPath (Join-Path $OpenWorkRuntimeDirectory 'node.exe') -Destination $openWorkOut -Force
Copy-Item -LiteralPath (Join-Path $OpenWorkRuntimeDirectory 'engine/opencode.exe') -Destination (Join-Path $openWorkOut 'engine') -Force
# Copy package contents into ordinary directories so the installer needs no pnpm junctions.
foreach ($package in @(@('openwork-server','0.18.57'), @('jsonc-parser','3.3.1'))) {
    $packageName = $package[0]
    $source = Join-Path $OpenWorkRuntimeDirectory "node_modules/.pnpm/$packageName@$($package[1])/node_modules/$packageName"
    $destination = Join-Path $openWorkOut "node_modules/$packageName"
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    Get-ChildItem -LiteralPath $source | Where-Object Name -ne 'node_modules' | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $destination -Recurse -Force
    }
}
Copy-Item -LiteralPath (Join-Path $root 'packaging/openwork/licenses') -Destination $openWorkOut -Recurse -Force

$workspaceDist = Join-Path $root 'apps/companion-workspace/dist'
foreach ($required in @('index.html', 'server.mjs', 'assets')) {
    if (-not (Test-Path -LiteralPath (Join-Path $workspaceDist $required))) { throw "Build companion workspace first; missing dist/$required" }
}
$workspaceOut = Join-Path $out 'runtime/workspace'
$chromeOut = Join-Path $out 'chrome-extension'
$legacyWorkspaceOut = Join-Path $out 'companion-workspace'
foreach ($stalePath in @($workspaceOut, $legacyWorkspaceOut)) {
    $resolvedStalePath = [IO.Path]::GetFullPath($stalePath)
    if (-not $resolvedStalePath.StartsWith([IO.Path]::GetFullPath($out) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean workspace path outside publish output: $resolvedStalePath"
    }
    if (Test-Path -LiteralPath $resolvedStalePath) { Remove-Item -LiteralPath $resolvedStalePath -Recurse -Force }
}
New-Item -ItemType Directory -Force -Path $workspaceOut, $chromeOut | Out-Null
Copy-Item -Path (Join-Path $workspaceDist '*') -Destination $workspaceOut -Recurse -Force
Copy-Item -Path (Join-Path $root 'apps/chrome-extension/*') -Destination $chromeOut -Recurse -Force
& (Join-Path $PSScriptRoot 'collect-third-party-licenses.ps1') -WorkspaceRoot (Join-Path $root 'apps/companion-workspace') -OpenWorkLicenseRoot (Join-Path $root 'packaging/openwork/licenses') -NpmFallbackRoot (Join-Path $root 'packaging/third-party-license-fallbacks/npm') -OutputDirectory (Join-Path $out 'THIRD-PARTY-LICENSES')

$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "Published: $exe ($sizeMb MB)"
Write-Host "Alias:     $out\OpenClicky.exe"
Write-Host "Copy that folder to a Windows 11 machine and run OpenClicky.exe (or OpenClicky.App.exe)."
