# Contributing to OpenClicky

## Dev loop

```powershell
dotnet build OpenClicky.sln -c Debug
dotnet test tests/OpenClicky.Core.Tests
dotnet run --project apps/windows/OpenClicky.App -c Debug
```

Linux/macOS agents can build/test with `EnableWindowsTargeting` (already in `Directory.Build.props`). Live tray/hotkey/overlay needs Windows 11.

## Scope

MVP: Talk + POINT overlay + confirmed Agent, client-only BYO OpenRouter keys, remappable Win+Alt+Space, EXE/MSIX.

Clean-room only — MIT Clicky + public HeyClicky UX. No commercial binary reverse-engineering.

## Packaging

- EXE: `scripts/publish-exe.ps1` / `scripts/publish-exe.sh` → `artifacts/exe/win-x64/`
- MSIX: `scripts/pack-msix.ps1` (unsigned; auto-finds Windows SDK `makeappx`) → `artifacts/msix/OpenClicky.msix`
- CI uploads both as `openclicky-win-x64` and `openclicky-msix`

See `docs/packaging.md` and `docs/agent-safety.md`.
