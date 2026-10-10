# OpenClicky

Open-source Windows AI buddy — screen-aware voice help, cursor overlay pointing, and (soon) confirmed computer-use agents.

Inspired by the public UX of [HeyClicky](https://www.heyclicky.com/) and the MIT architecture of [farzaa/clicky](https://github.com/farzaa/clicky). **Clean-room:** we do not reverse-engineer commercial binaries.

## Status

**Phase A spike** — tray app, remappable push-to-talk (default `Win+Alt+Space`), click-through overlay, in-memory screen capture, optional localhost OpenRouter proxy.

| Layer | Notes |
|-------|--------|
| Client | .NET 8 Windows (`OpenClicky.sln`) — **build/run on Windows 11 x64** |
| Core tests | `dotnet test` works on any OS with .NET 8 |
| Local proxy | Node 20+ — `services/local-proxy` |

## Quick start (Windows 11)

```powershell
# Prerequisites: .NET 8 SDK
dotnet build OpenClicky.sln -c Debug
dotnet run --project apps/windows/OpenClicky.App -c Debug
```

1. Find **OpenClicky** in the system tray.  
2. Hold **Win+Alt+Space** — buddy overlay appears.  
3. Release — overlay fades; Settings → Refresh log should show `capture_bytes=… magic=FFD8`.  
4. Remap the hotkey under **Settings…**.

Optional proxy (any OS):

```bash
cd services/local-proxy
npm install
export OPENROUTER_API_KEY=sk-or-...   # only needed for /chat
npm start
curl http://127.0.0.1:8787/health
```

## Repo layout

See the project plan in the OpenClicky Agent Store (`docs/windows-heyclicky-plan.md`) for product decisions. In-repo:

```
apps/windows/          # .NET solution projects
services/local-proxy/  # OpenRouter-first localhost sidecar
tests/                 # Core unit tests
docs/                  # Contributor docs
```

## Privacy (v1)

- **Client-only BYO keys** — no OpenClicky cloud.  
- Screenshots stay in memory for the request (Phase A never writes them).  
- Optional local proxy keeps keys off the UI process if you prefer.

## License

MIT — see [LICENSE](LICENSE).
