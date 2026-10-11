# Packaging

## EXE (recommended for testing)

On Windows 11 with .NET 8 SDK:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish-exe.ps1
```

Output: `artifacts/exe/win-x64/OpenClicky.App.exe` (self-contained single-file).

From Linux CI (cross-compile):

```bash
bash scripts/publish-exe.sh
```

**Signing:** no Authenticode cert is applied in this repo. SmartScreen may warn on first run — expected for unsigned OSS builds. To sign locally once you have a cert:

```powershell
signtool sign /fd SHA256 /a /tr http://timestamp.digicert.com /td SHA256 artifacts\exe\win-x64\OpenClicky.App.exe
```

## MSIX (unsigned by default)

Identity/manifest: `packaging/msix/Package.appxmanifest`.

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish-exe.ps1
powershell -ExecutionPolicy Bypass -File scripts/pack-msix.ps1
# → artifacts/msix/OpenClicky.msix
```

**This MSIX is intentionally unsigned** until a code-signing certificate is available. Sideload options:

1. Sign with `signtool` + your cert, then `Add-AppxPackage`.  
2. Or enable Developer Mode and sideload per your org policy.  
3. Microsoft Store submission remains post-MVP.

There is no CI signing secret in this repository — do not expect a signed Store package from `pack-msix.ps1` alone.

## CI artifact

Every push to `main` / `cursor/**` runs `.github/workflows/windows-ci.yml` on `windows-latest` and uploads `openclicky-win-x64` (contents of `artifacts/exe/win-x64/`). Download from the Actions run — still unsigned.

## Smoke checklist after publish

1. Run EXE → tray icon appears (single-instance; second launch shows a dialog).  
2. Settings → OpenRouter key (Credential Manager). Debug log also at `%AppData%\OpenClicky\debug.log`.  
3. Hold Win+Alt+Space (or remapped chord) → Talk reply; log shows `capture_backend=wgc` when Graphics Capture works.  
4. `dotnet run --project apps/windows/OpenClicky.Sandbox` + `openclicky agent click the green button` → confirm dialogs → act.
