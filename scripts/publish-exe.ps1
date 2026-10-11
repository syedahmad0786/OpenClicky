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

Write-Host "Published: $out\OpenClicky.App.exe"
Write-Host "Copy that folder to a Windows 11 machine and run OpenClicky.App.exe"
