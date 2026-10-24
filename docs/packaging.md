# Packaging

## Portable app

From the repository root on Windows, with a .NET 8 SDK or newer:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish-exe.ps1
```

The self-contained 0.6.0 x64 preview is published to `artifacts/phase-one-preview/win-x64/`. This separate path preserves existing 0.5 artifacts and installed user data. `OpenClicky.exe` is the friendly alias; `OpenClicky.App.exe` is the canonical executable used by the MSIX manifest. Run one, not both. Copy the whole folder; no .NET runtime installation is required.

The preview always includes the verified local OpenWork 0.18.57 runtime, its internal OpenCode 1.18.30 engine, Node.js, and the built companion workspace. To use a separately staged verified OpenWork runtime:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish-exe.ps1 -OpenWorkRuntimeDirectory artifacts/runtime/openwork
```

Publishing runs the existing OpenWork verification script and then copies ordinary files rather than pnpm junctions. It generates `THIRD-PARTY-LICENSES/manifest.json` from the installed production pnpm and resolved NuGet graphs, copies their shipped license and notice files, and retains the OpenWork, OpenCode, and Node runtime notices. For npm packages that omit license text, the tracked fallback inventory records the exact npm version and either the package repository commit containing the license or the package's declared SPDX identifier plus canonical text at a pinned SPDX repository commit. Publishing verifies every fallback hash. Both unresolved lists must be empty. Direct standalone OpenCode 1.18.0 packaging is no longer supported because OpenClicky uses the single OpenWork runtime path.

`runtime/workspace/` contains the previously built `server.mjs`, `index.html`, and browser assets. Packaged assets resolve beside the executable, including when .NET extracts the single-file application's assemblies elsewhere. `chrome-extension/` contains the optional guided local Chrome bridge setup; it is not installed or authorized by publishing. Rebuild and test the workspace before packaging whenever its source changes.

For a zip:

```powershell
Compress-Archive -Path artifacts/phase-one-preview/win-x64/* -DestinationPath artifacts/phase-one-preview/OpenClicky-0.6.0-win-x64.zip -Force
```

Linux/macOS cross-publishing remains available with `bash scripts/publish-exe.sh`; interactive Windows acceptance must happen on Windows.

Whisper's native loader looks for libraries relative to its assembly. The publish scripts extract bundled content to keep that path available in the portable EXE; the dedicated speech harness was also tested in this publish mode. The voice model is downloaded separately under LocalAppData. Local Whisper requires an AVX2-capable Windows 11 x64 CPU and the Visual C++ 2022 runtime; check these on a clean machine before distribution.

The executable includes the original OpenClicky icon. The package carries proper 44, 50, and 150 pixel Windows logo assets, generated from the same artwork. `scripts/generate-brand.py` regenerates them using Pillow; running the generator is not required for a normal build.

## MSIX

After publishing:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/pack-msix.ps1
```

Output: `artifacts/phase-one-preview/msix/OpenClicky-0.6.0-unsigned.msix`. The script finds `makeappx.exe` on PATH or in the installed Windows SDK. The manifest is under `packaging/msix/`; staging is restricted beneath repository `artifacts/`.

MSIX staging includes the complete portable output: OpenWork, companion workspace, Chrome setup files, notices, and executables. Existing 0.4.1 and 0.5.0 artifacts remain historical and are not overwritten.

The EXE and MSIX are **unsigned**. Local package creation does not prove the MSIX is installed or trusted. For normal distribution, sign with your code-signing certificate and verify installation on a clean Windows machine. No signing credentials or Microsoft Store release are configured in this repository. Do not weaken a user's Windows security settings as part of an automated test.

## Release checks

1. Launch the published EXE and inspect the home screen, icon, tray, and settings at the target display scaling.
2. Run both built-in demos; inspect the real destination controls rather than trusting a success log.
3. Connect your provider yourself and run the [live acceptance checklist](verification.md).
4. Test the physical microphone and remapped shortcut. Test cancellation during processing, speech, and confirmation.
5. Verify signed installation and upgrade behavior separately before distributing an installer.

Clean-machine launch, WebView2 availability, Chrome extension loading, native-host registration, MSIX trust, installation, and upgrade behavior remain manual acceptance items. Packaging success does not prove them.

GitHub Actions publishes the `openclicky-win-x64` folder and unsigned `openclicky-msix` artifact. Check the run for the exact commit being distributed; the October 2026 local verification record does not claim that new changes have run in GitHub CI.
