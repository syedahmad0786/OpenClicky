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

## MSIX (later / Store)

MSIX packaging is deferred until the WinUI settings shell lands. For now:

1. Ship the published EXE folder.  
2. Optional: wrap with [Microsoft Windows App SDK MSIX packaging](https://learn.microsoft.com/windows/msix/desktop/desktop-to-uwp-packaging-dot-net) once identity/certs exist.  
3. Store submission is explicitly post-MVP for this OSS project.

## Smoke checklist after publish

1. Run EXE → tray icon appears.  
2. Settings → OpenRouter key (or debug transcript).  
3. Hold Win+Alt+Space → Talk reply.  
4. `openclicky agent open notepad` → confirm dialogs → type.
