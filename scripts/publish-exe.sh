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

exe="$out/OpenClicky.App.exe"
if [[ ! -f "$exe" ]]; then
  echo "Expected publish output missing: $exe" >&2
  exit 1
fi

# Friendly alias for manual installs (MSIX still launches OpenClicky.App.exe).
cp -f "$exe" "$out/OpenClicky.exe"
cp -f "$root/LICENSE" "$root/THIRD-PARTY-NOTICES.md" "$out/"
cp -f "$root/docs/portable-quick-start.md" "$out/README.md"

size_mb=$(du -m "$exe" | awk '{print $1}')
echo "Published: $exe (${size_mb} MB)"
echo "Alias:     $out/OpenClicky.exe"
