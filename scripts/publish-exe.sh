#!/usr/bin/env bash
# Cross-publish win-x64 from Linux/macOS CI (EnableWindowsTargeting).
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
out="${OUTPUT_DIRECTORY:-$root/artifacts/phase-one-preview/win-x64}"
openwork="${OPENWORK_RUNTIME_DIRECTORY:-$root/artifacts/runtime/openwork}"
mkdir -p "$out"

dotnet publish "$root/apps/windows/OpenClicky.App/OpenClicky.App.csproj" \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:IncludeAllContentForSelfExtract=true \
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

for required in node.exe engine/opencode.exe node_modules/.pnpm/openwork-server@0.18.57/node_modules/openwork-server node_modules/.pnpm/jsonc-parser@3.3.1/node_modules/jsonc-parser; do
  [[ -e "$openwork/$required" ]] || { echo "Missing verified OpenWork runtime item: $required" >&2; exit 1; }
done
mkdir -p "$out/runtime/openwork/engine" "$out/runtime/openwork/node_modules"
cp -f "$openwork/node.exe" "$out/runtime/openwork/"
cp -f "$openwork/engine/opencode.exe" "$out/runtime/openwork/engine/"
cp -a "$openwork/node_modules/.pnpm/openwork-server@0.18.57/node_modules/openwork-server" "$out/runtime/openwork/node_modules/"
cp -a "$openwork/node_modules/.pnpm/jsonc-parser@3.3.1/node_modules/jsonc-parser" "$out/runtime/openwork/node_modules/"
cp -a "$root/packaging/openwork/licenses" "$out/runtime/openwork/"
[[ -f "$root/apps/companion-workspace/dist/index.html" && -f "$root/apps/companion-workspace/dist/server.mjs" && -d "$root/apps/companion-workspace/dist/assets" ]] || { echo "Build companion workspace first." >&2; exit 1; }
rm -rf -- "$out/runtime/workspace" "$out/companion-workspace"
mkdir -p "$out/runtime/workspace" "$out/chrome-extension"
cp -a "$root/apps/companion-workspace/dist/." "$out/runtime/workspace/"
cp -a "$root/apps/chrome-extension/." "$out/chrome-extension/"
command -v pwsh >/dev/null || { echo "pwsh is required to generate the third-party license inventory." >&2; exit 1; }
pwsh -NoProfile -File "$root/scripts/collect-third-party-licenses.ps1" \
  -WorkspaceRoot "$root/apps/companion-workspace" \
  -OpenWorkLicenseRoot "$root/packaging/openwork/licenses" \
  -NpmFallbackRoot "$root/packaging/third-party-license-fallbacks/npm" \
  -OutputDirectory "$out/THIRD-PARTY-LICENSES"

size_mb=$(du -m "$exe" | awk '{print $1}')
echo "Published: $exe (${size_mb} MB)"
echo "Alias:     $out/OpenClicky.exe"
