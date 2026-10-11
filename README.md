# OpenClicky

Open-source Windows AI buddy — screen-aware voice help, cursor overlay pointing, and (soon) confirmed computer-use agents.

Inspired by the public UX of [HeyClicky](https://www.heyclicky.com/) and the MIT architecture of [farzaa/clicky](https://github.com/farzaa/clicky). **Clean-room:** we do not reverse-engineer commercial binaries.

## Status

**Phase B (Talk)** in progress on top of the Phase A shell.

| Capability | Status |
|------------|--------|
| Tray + remappable PTT (`Win+Alt+Space`) | Done |
| Click-through overlay buddy | Done |
| In-memory screen capture | Done |
| OpenRouter vision Talk + POINT parse | Done (needs API key) |
| STT via OpenRouter Whisper-compatible API | Done (needs API key) |
| Local Windows SAPI TTS | Done (no key) |
| Optional localhost proxy | Done |
| Agent computer-use | Phase D |

## Quick start (Windows 11)

```powershell
dotnet build OpenClicky.sln -c Debug
dotnet run --project apps/windows/OpenClicky.App -c Debug
```

1. Tray icon → **Settings…** → paste **OpenRouter API key** → Save.  
2. Optional: set a **debug transcript** (e.g. `what is on my screen?`) to skip mic/STT while testing.  
3. Hold **Win+Alt+Space**, speak (or rely on debug transcript), release.  
4. OpenClicky captures the screen, calls OpenRouter, speaks the reply locally, and may point the buddy.

Optional proxy:

```bash
cd services/local-proxy && npm install && npm start
# Enable "Use local proxy" in Settings (http://127.0.0.1:8787)
```

## Privacy

- Client-only BYO keys — no OpenClicky cloud.  
- Settings live in `%AppData%\OpenClicky\settings.json`.  
- Screenshots stay in memory for the request.

## License

MIT — see [LICENSE](LICENSE).
