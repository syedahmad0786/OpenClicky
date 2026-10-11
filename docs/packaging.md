# Packaging

## EXE (recommended for testing)

On Windows 11 with .NET 8 SDK:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish-exe.ps1
```

Output:

- `artifacts/exe/win-x64/OpenClicky.App.exe` — self-contained single-file (canonical; MSIX entry point)
- `artifacts/exe/win-x64/OpenClicky.exe` — copy alias for manual installs

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

`pack-msix.ps1` locates `makeappx.exe` on `PATH` or under `%ProgramFiles(x86)%\Windows Kits\10\bin\*\x64\`.

**This MSIX is intentionally unsigned** until a code-signing certificate is available. Sideload options:

1. Sign with `signtool` + your cert, then `Add-AppxPackage`.  
2. Or enable **Developer Mode** (`Settings → Privacy & security → For developers`) and:

```powershell
Add-AppxPackage -Path artifacts\msix\OpenClicky.msix -AllowUnsigned
```

(AllowUnsigned requires a recent Windows 11 build; otherwise sign first.)  
3. Microsoft Store submission remains post-MVP.

There is no CI signing secret in this repository — do not expect a signed Store package from `pack-msix.ps1` alone.

## CI artifacts

Every push to `main` / `cursor/**` (and PRs into `main`) runs `.github/workflows/windows-ci.yml` on `windows-latest` and uploads:

| Artifact | Contents |
|----------|----------|
| `openclicky-win-x64` | Folder `artifacts/exe/win-x64/` (`OpenClicky.exe` + `OpenClicky.App.exe` + deps) |
| `openclicky-msix` | `artifacts/msix/OpenClicky.msix` (unsigned) |

Download from the Actions run for that commit — still unsigned.

### Install / run from CI (Ahmad / Windows 11)

**EXE (fastest):**

1. Open the green `windows-ci` run → Artifacts → download `openclicky-win-x64`.  
2. Unzip to e.g. `%USERPROFILE%\Apps\OpenClicky\`.  
3. Double-click `OpenClicky.exe` (or `OpenClicky.App.exe`).  
4. If SmartScreen blocks: **More info → Run anyway**.  
5. Tray icon → Settings → OpenRouter key **or** Continue with ChatGPT → hold **Win+Alt+Space**.  
   (CredWrite/DPAPI fix is on phase-a `2cae07c` — ChatGPT token save should work for oversized OAuth blobs.)

**MSIX (optional sideload):**

1. Download artifact `openclicky-msix`.  
2. Enable Developer Mode.  
3. `Add-AppxPackage -Path .\OpenClicky.msix -AllowUnsigned` (or sign first).  
4. Launch **OpenClicky** from the Start menu.

## Smoke checklist after publish

1. Run EXE → tray icon appears (single-instance; second launch shows a dialog).  
2. Settings → OpenRouter key (Credential Manager). Debug log also at `%AppData%\OpenClicky\debug.log`.  
3. Hold Win+Alt+Space (or remapped chord) → Talk reply; log shows `capture_backend=wgc` when Graphics Capture works.  
4. `dotnet run --project apps/windows/OpenClicky.Sandbox` + `openclicky agent click the green button` → confirm dialogs → act.
