# Packaging

## Portable app

From the repository root on Windows, with a .NET 8 SDK or newer:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish-exe.ps1
```

The self-contained x64 app is published to `artifacts/exe/win-x64/`. `OpenClicky.exe` is the friendly alias; `OpenClicky.App.exe` is the canonical executable used by the MSIX manifest. Run one, not both. Copy the whole folder to the destination, including the license notices and quick-start README. No .NET runtime installation is required to launch it.

For a zip:

```powershell
Compress-Archive -Path artifacts/exe/win-x64/* -DestinationPath artifacts/OpenClicky-0.2-win-x64.zip -Force
```

Linux/macOS cross-publishing remains available with `bash scripts/publish-exe.sh`; interactive Windows acceptance must happen on Windows.

The executable includes the original OpenClicky icon. The package carries proper 44, 50, and 150 pixel Windows logo assets, generated from the same artwork. `scripts/generate-brand.py` regenerates them using Pillow; running the generator is not required for a normal build.

## MSIX

After publishing:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/pack-msix.ps1
```

Output: `artifacts/msix/OpenClicky.msix`. The script finds `makeappx.exe` on PATH or in the installed Windows SDK. The manifest is under `packaging/msix/`; staging is restricted to the repository's package staging directory.

The EXE and MSIX are **unsigned**. Local package creation does not prove the MSIX is installed or trusted. For normal distribution, sign with your code-signing certificate and verify installation on a clean Windows machine. No signing credentials or Microsoft Store release are configured in this repository. Do not weaken a user's Windows security settings as part of an automated test.

## Release checks

1. Launch the published EXE and inspect the home screen, icon, tray, and settings at the target display scaling.
2. Run both built-in demos; inspect the real destination controls rather than trusting a success log.
3. Connect your provider yourself and run the [live acceptance checklist](verification.md).
4. Test the physical microphone and remapped shortcut. Test cancellation during processing, speech, and confirmation.
5. Verify signed installation and upgrade behavior separately before distributing an installer.

GitHub Actions publishes the `openclicky-win-x64` folder and unsigned `openclicky-msix` artifact. Check the run for the exact commit being distributed; the October 2026 local verification record does not claim that new changes have run in GitHub CI.
