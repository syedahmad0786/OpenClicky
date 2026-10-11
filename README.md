# OpenClicky

<img src="assets/openclicky.png" alt="OpenClicky green pointer companion" width="112" />

An open-source Windows companion that can explain your screen, point things out, and perform short desktop tasks with your approval. MIT licensed, with your own AI provider account.

Inspired by [HeyClicky](https://www.heyclicky.com/) and the public MIT project [farzaa/clicky](https://github.com/farzaa/clicky). OpenClicky has its own branding and Windows implementation. It is an independent project.

## Try it

Windows 11, x64. The portable release includes its runtime; no .NET installation is needed to run it.

1. Extract the published folder and open **OpenClicky.exe**.
2. Try **Pointing demo** or **Test desktop actions**. These clearly labeled demos work without an account. Approve each test action and inspect the green button count and the exact text `hello`.
3. In **Settings → Connection**, add your OpenRouter API key yourself, choose a vision model, and save. Keys are stored using Windows Credential Manager.
4. Type a request in **Ask**, or hold **Win+Alt+Space**, speak, and release. Typed requests give you three seconds to select the app you want help with.
5. For a short task, select **Agent**. Review each proposed click, typing action, hotkey, or scroll. **Stop** or **Escape** cancels the task; declining an action ends the run.

The window closes to the tray. Double-click the tray icon to reopen it; choose **Quit** from the tray menu to exit. The voice shortcut can be changed in Settings. Escape is reserved for stopping.

Windows speech recognition and spoken replies run locally by default. An installed Windows speech recognizer is required for voice input; typing always remains available. Optional OpenRouter transcription sends recorded audio to that provider. ChatGPT sign-in is an experimental alternative and has not been verified live in this release.

## Release status

**0.2 is a working Windows preview, with desktop verification and live-provider acceptance still separate.**

| Capability | Evidence / limit |
| --- | --- |
| Home, tray, settings, original icons | Visually inspected on Windows 11 at this machine's display scaling |
| Typed Ask and Agent requests | Shared cancellable pipeline; missing-provider errors are visible |
| Pointing overlay | Actual overlay position verified, including a monitor left of the primary display |
| Desktop actions with human approval | Built-in test clicked the green button and typed exact `hello`; decline stops input |
| Screen capture | Actual in-memory JPEG capture on two monitors using WGC |
| Local speech | Installed recognizer tested with generated speech; real TTS cancellation tested |
| OpenRouter Talk / Agent | Implemented; no live request verified for this release yet |
| Physical microphone / voice shortcut | Requires the human acceptance check |
| Portable EXE / MSIX | Built locally; unsigned; MSIX installation not verified |

OpenClicky does not yet provide HeyClicky's persistent named companions, drawing canvas, routines, scheduling, dictation into apps, or account connectors. See the [video research and scope](docs/heyclicky-research.md) and [verification record](docs/verification.md).

## Privacy and control

- Requests capture **all connected monitors** and send their images to your selected AI provider. Choose what is visible before starting. Images are held in memory rather than saved by OpenClicky.
- There is no OpenClicky-hosted service. Providers have their own processing and retention policies; local capture does not make the AI request local.
- API keys use Credential Manager. Oversized ChatGPT token records use a DPAPI-protected file. Non-secret settings live in `%AppData%\OpenClicky\settings.json`.
- Optional text history is enabled by default and saved in `%AppData%\OpenClicky\history.json`; turn it off or clear it in Settings. Routine logs contain sizes, states, and error classes rather than request or response contents. Existing logs from older versions are not deleted automatically.
- Every input action requires approval. The window-title deny-list is a convenience guard, not a security boundary. Review the destination and action each time. See [agent safety](docs/agent-safety.md).

## Build from source

Use a .NET 8 SDK or newer on Windows:

```powershell
dotnet build OpenClicky.sln -c Release
dotnet test OpenClicky.sln -c Release --no-build
dotnet run --project apps/windows/OpenClicky.App -c Release
```

Publish the portable app and optional unsigned MSIX:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish-exe.ps1
powershell -ExecutionPolicy Bypass -File scripts/pack-msix.ps1
```

Outputs are under `artifacts/exe/win-x64/` and `artifacts/msix/`. [Packaging](docs/packaging.md) explains distribution and signing. CI in [.github/workflows/windows-ci.yml](.github/workflows/windows-ci.yml) builds, tests, runs dedicated capture/action/overlay checks, and publishes artifacts; that is not a substitute for human acceptance.

The optional existing localhost proxy is under `services/local-proxy`. It only accepts an explicitly configured loopback URL. The normal app calls the selected provider directly.

## Development checks

The built-in demos are the easiest manual check. Automated desktop checks use their own sandbox window; automatic approval exists only in the dedicated test executable. The legacy `OPENCLICKY_OFFLINE_TALK` and `OPENCLICKY_OFFLINE_AGENT` environment fixtures remain for development, with an explicit offline label. Debug transcript overrides are ignored on normal launches so an old fixture cannot silently replace microphone input.

See [architecture](docs/architecture.md) and [verification](docs/verification.md) for the test commands and acceptance checklist. Earlier phase documents are historical implementation notes.

## License

[MIT](LICENSE). The adapted public companion prompt retains its original [third-party notice](THIRD-PARTY-NOTICES.md). No HeyClicky logo, commercial binary, or marketing video is included in the release.
