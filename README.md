# OpenClicky

Open-source Windows AI buddy — screen-aware voice help, cursor overlay pointing, and (soon) confirmed computer-use agents.

Inspired by the public UX of [HeyClicky](https://www.heyclicky.com/) and the MIT architecture of [farzaa/clicky](https://github.com/farzaa/clicky). **Clean-room:** we do not reverse-engineer commercial binaries.

## CI

GitHub Actions (`.github/workflows/windows-ci.yml`) on `windows-latest`: build solution, run unit tests, capture smoke, **agent↔sandbox smoke** (confirm-gated click/type with test-only auto-confirm), **overlay POINT smoke** (buddy tip HWND at virtual target), publish unsigned win-x64 EXE artifact `openclicky-win-x64`. Tray/PTT/Talk/spoken POINT still need the human Win11 checklist below.

## Status

**MVP in progress** — Talk + POINT fly-to + confirmed Agent on the Phase A shell.

| Capability | Status |
|------------|--------|
| Tray + remappable PTT (`Win+Alt+Space`) | Done |
| Click-through overlay buddy + POINT fly-to | Done |
| In-memory screen capture | Done (WGC+D3D11 frame-pool; GDI fallback) |
| OpenRouter vision Talk + POINT parse | Done (BYO API key) |
| Sign in with ChatGPT (OSS OAuth / PKCE) | Done (alternate Talk/Agent auth; tokens in Credential Manager) |
| STT via OpenRouter Whisper-compatible API | Done (BYO key; ChatGPT sign-in does not replace mic STT) |
| Local Windows SAPI TTS | Done (no key) |
| Optional localhost proxy | Done |
| Agent (plan → confirm → UIA/SendInput) | Done (Windows + OpenRouter key **or** ChatGPT sign-in) |
| Credential Manager for API keys / ChatGPT tokens | Done |
| Soft agent deny-list | Done |
| Text Talk history persistence | Done |
| Agent sandbox demo app | Done |
| Self-contained EXE publish scripts | Done (unsigned) |
| MSIX | Manifest + pack script (unsigned stub) |

## Quick start (Windows 11)

```powershell
dotnet build OpenClicky.sln -c Debug
dotnet run --project apps/windows/OpenClicky.App -c Debug
```

Or download the unsigned `openclicky-win-x64` artifact from [windows-ci](.github/workflows/windows-ci.yml) Actions runs / publish locally:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish-exe.ps1
# → artifacts/exe/win-x64/OpenClicky.App.exe
```

See [docs/packaging.md](docs/packaging.md) and [docs/agent-safety.md](docs/agent-safety.md).

Optional proxy:

```bash
cd services/local-proxy && npm install && npm start
# Enable "Use local proxy" in Settings (http://127.0.0.1:8787)
```

## Windows 11 smoke checklist (human)

CI validates build/tests/capture on `windows-latest` — it does **not** exercise the tray, PTT hotkey, overlay, STT, or Agent confirms. Run this on a real interactive Windows 11 desktop:

1. **Launch**  
   `dotnet run --project apps/windows/OpenClicky.App -c Debug`  
   **Pass:** tray icon “OpenClicky” appears; `%AppData%\OpenClicky\debug.log` gets a `tray ready` line. Second launch shows “already running”.

2. **Auth**  
   Tray → Settings → either paste **OpenRouter API key** → Save, **or** click **Continue with ChatGPT** (browser OAuth) and select the ChatGPT provider radio.  
   Optional: set debug transcript `what is on my screen?` to skip mic (mic STT still needs an OpenRouter key).  
   **Pass:** log shows `openrouter_key=set` and/or `chatgpt=you@…`; secrets are **not** in `settings.json` (Credential Manager only).

3. **Talk**  
   Open Notepad with some text visible. Hold **Win+Alt+Space**, speak (or use debug transcript), release.  
   **Pass:** overlay listening → processing; spoken reply; debug log has `capture_backend=wgc` (or `gdi` if forced), `transcript=…`, `reply=…`.

4. **POINT**  
   Ask where a visible UI control is (or use a debug transcript that prompts pointing).  
   **Pass:** debug log has `point=(x,y)`; blue buddy **flies to** the control and **holds there while TTS speaks** (does not snap back to the cursor). CI covers HWND settle after `PointAt`+`ShowResponding` (`OpenClicky.Overlay.Smoke`); this checklist covers the Talk-orchestrated path.

5. **Hotkey remap**  
   Settings → set PTT to **Ctrl+Shift+Space** → Save.  
   **Pass:** new chord triggers Talk; old Win+Alt+Space does not.

6. **Agent**  
   `dotnet run --project apps/windows/OpenClicky.Sandbox`  
   Focus the sandbox, then (debug transcript or voice): `openclicky agent click the green button then type hello`.  
   **Pass:** Yes/No confirm per mutating step; green button click count increases; text appears in the field; no crash.

7. **Privacy**  
   After a Talk turn, check `%AppData%\OpenClicky\` and Temp.  
   **Pass:** no screenshot `.jpg`/`.png` written by OpenClicky (history JSON text-only is OK).

## Privacy

- Client-only BYO keys — no OpenClicky cloud.  
- OpenRouter API key → **Windows Credential Manager** (`OpenClicky/openrouter-api-key`).  
- Non-secret settings → `%AppData%\OpenClicky\settings.json` (never writes the API key).  
- Screenshots stay in memory for the request.  
- Optional text-only conversation history → `%AppData%\OpenClicky\history.json` (toggle/clear in Settings).  
- Agent soft-blocks mutating actions over banking / password-manager window titles.

## License

MIT — see [LICENSE](LICENSE).
