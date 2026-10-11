# OpenClicky architecture

```
Tray (WinForms) ── TalkOrchestrator ──► OpenRouter (vision/STT)
                 │                   └── SAPI TTS + POINT overlay
                 └── AgentOrchestrator ──► planner JSON
                                         ├── UIA snapshot (read)
                                         └── SendInput click/type/hotkey/scroll (confirm)
```

| Project | Role |
|---------|------|
| `OpenClicky.App` | Tray host, settings, Credential Manager, TTS |
| `OpenClicky.Core` | Orchestrators, POINT, settings, deny-list, history |
| `OpenClicky.Capture` | In-memory JPEG capture (GDI; WGC probe) |
| `OpenClicky.Input` | Remappable PTT |
| `OpenClicky.Overlay` | Click-through buddy + fly-to |
| `OpenClicky.Providers` | OpenRouter clients |
| `OpenClicky.Agent` | UIA + SendInput tools |
| `OpenClicky.Sandbox` | Agent demo UI |
| `services/local-proxy` | Optional localhost OpenRouter sidecar |

**Privacy defaults:** BYO keys in Credential Manager; screenshots never written; history is text-only and optional.
