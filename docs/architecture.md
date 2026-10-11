# OpenClicky architecture

```text
Home text / global push-to-talk
             │
       TalkOrchestrator (one cancellable turn)
             ├─ local Windows speech / optional OpenRouter audio transcription
             ├─ in-memory monitor capture → vision provider → text + POINT
             └─ AgentOrchestrator → bounded plan / approve / execute / recapture
                                     ├─ UIA snapshot (read)
                                     └─ SendInput (human approval)
Replies → Home conversation + local SAPI voice + click-through pointing overlay
```

| Project | Role |
| --- | --- |
| `OpenClicky.App` | Home and tray host, settings, human confirmation, local speech, secure storage |
| `OpenClicky.Core` | Shared turn pipeline, agent loop, POINT mapping, settings, deny-list, text history |
| `OpenClicky.Capture` | WGC/D3D11 monitor capture with GDI fallback; in-memory JPEGs |
| `OpenClicky.Input` | Remappable global push-to-talk and Escape cancellation |
| `OpenClicky.Overlay` | Click-through companion, fly-to, labels, speech bubble |
| `OpenClicky.Providers` | ChatGPT OAuth, account-specific model discovery, and streamed Responses; optional OpenRouter vision/transcription |
| `OpenClicky.Agent` | UIA snapshots, focus restoration, SendInput tools |
| `OpenClicky.Sandbox` | Reversible action test window |
| `*.Smoke` | Separate capture, overlay, action, and secret-store/speech checks |
| `services/local-proxy` | Existing optional localhost provider sidecar |

Text and voice use the same pipeline. A new request cancels the old one; a semaphore prevents overlapping AI turns. The overlay hides synchronously before screenshots and holds its pointed location until speech finishes. Turning speech off gives the visual response a brief display interval.

Agent input is bounded to eight steps. Invalid tools or coordinates are rejected. Decline, failed execution, or loss of target focus stops the run. The confirmation restores both the target window and its focused child control before input. The built-in sandbox is the only permitted in-process input target. Each subsequent step captures the desktop again; the planner is instructed to inspect the result before declaring success. This is still model behavior, not a general correctness guarantee.

Settings and text history are written through a temporary file and replaced atomically. History is capped at ten Talk turns. Screenshots stay in memory and API keys are excluded from settings JSON. Saved history is plaintext, optional, and enabled by default. See [agent safety](agent-safety.md) for the distinction between title guards, human approval, and security boundaries.
