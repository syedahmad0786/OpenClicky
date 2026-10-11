#!/usr/bin/env bash
# Cross-publish win-x64 from Linux/macOS CI (EnableWindowsTargeting).
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
out="$root/artifacts/exe/win-x64"
mkdir -p "$out"

dotnet publish "$root/apps/windows/OpenClicky.App/OpenClicky.App.csproj" \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -p:EnableWindowsTargeting=true \
  -o "$out"

echo "Published: $out/OpenClicky.App.exe"
