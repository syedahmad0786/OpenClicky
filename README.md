# OpenClicky

Open-source Windows AI buddy — screen-aware voice help, cursor overlay pointing, and (soon) confirmed computer-use agents.

Inspired by the public UX of [HeyClicky](https://www.heyclicky.com/) and the MIT architecture of [farzaa/clicky](https://github.com/farzaa/clicky). **Clean-room:** we do not reverse-engineer commercial binaries.

## Status

**MVP in progress** — Talk + POINT fly-to + confirmed Agent on the Phase A shell.

| Capability | Status |
|------------|--------|
| Tray + remappable PTT (`Win+Alt+Space`) | Done |
| Click-through overlay buddy + POINT fly-to | Done |
| In-memory screen capture | Done (GDI; WGC probed, frame-pool next) |
| OpenRouter vision Talk + POINT parse | Done (needs API key) |
| STT via OpenRouter Whisper-compatible API | Done (needs API key) |
| Local Windows SAPI TTS | Done (no key) |
| Optional localhost proxy | Done |
| Agent (plan → confirm → UIA/SendInput) | Done (needs Windows + key) |
| Credential Manager for API keys | Done |
| Soft agent deny-list | Done |
| Self-contained EXE publish scripts | Done (unsigned) |
| MSIX | Manifest + pack script (unsigned stub) |

## Quick start (Windows 11)

```powershell
dotnet build OpenClicky.sln -c Debug
dotnet run --project apps/windows/OpenClicky.App -c Debug
```

1. Tray icon → **Settings…** → paste **OpenRouter API key** → Save.  
2. Optional: set a **debug transcript** (e.g. `what is on my screen?`) to skip mic/STT while testing.  
3. Hold **Win+Alt+Space**, speak (or rely on debug transcript), release.  
4. OpenClicky captures the screen, calls OpenRouter, speaks the reply locally, and may point the buddy.  
5. Say `openclicky agent open notepad and type hello` → confirm each step → Agent uses UIA + SendInput.  
6. Optional Agent playground: `dotnet run --project apps/windows/OpenClicky.Sandbox` then ask the agent to click **Green button** / type into the field.

### Publish EXE (unsigned)

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

## Privacy

- Client-only BYO keys — no OpenClicky cloud.  
- OpenRouter API key → **Windows Credential Manager** (`OpenClicky/openrouter-api-key`).  
- Non-secret settings → `%AppData%\OpenClicky\settings.json` (never writes the API key).  
- Screenshots stay in memory for the request.  
- Optional text-only conversation history → `%AppData%\OpenClicky\history.json` (toggle/clear in Settings).  
- Agent soft-blocks mutating actions over banking / password-manager window titles.

## License

MIT — see [LICENSE](LICENSE).
