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

No code signing is applied yet — SmartScreen may warn on first run.

## MSIX (unsigned stub)

Identity/manifest live in `packaging/msix/Package.appxmanifest`. On Windows 11 with the Windows SDK (`makeappx` on PATH):

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish-exe.ps1
powershell -ExecutionPolicy Bypass -File scripts/pack-msix.ps1
# → artifacts/msix/OpenClicky.msix (unsigned)
```

Sign with `signtool` before broad sideload. Store submission remains post-MVP.

## Smoke checklist after publish

1. Run EXE → tray icon appears.  
2. Settings → OpenRouter key (or debug transcript).  
3. Hold Win+Alt+Space → Talk reply.  
4. `openclicky agent open notepad` → confirm dialogs → type.
