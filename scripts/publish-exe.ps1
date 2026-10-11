# Publish a self-contained win-x64 EXE for OpenClicky (unsigned).
# Run from repo root on Windows with .NET 8 SDK:
#   powershell -ExecutionPolicy Bypass -File scripts/publish-exe.ps1

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$out = Join-Path $root "artifacts/exe/win-x64"
New-Item -ItemType Directory -Force -Path $out | Out-Null

dotnet publish `
  (Join-Path $root "apps/windows/OpenClicky.App/OpenClicky.App.csproj") `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
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

$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "Published: $exe ($sizeMb MB)"
Write-Host "Alias:     $out\OpenClicky.exe"
Write-Host "Copy that folder to a Windows 11 machine and run OpenClicky.exe (or OpenClicky.App.exe)."
