# OpenClicky architecture

```text
Home text / voice button / global push-to-talk / optional app-switch guide
             │
       TalkOrchestrator (one cancellable turn)
             ├─ selected WASAPI microphone → local Whisper previews / compatibility STT
             ├─ current + previous app hints / small official app guides
             ├─ monitor images + legible active-window detail → vision → POINT + MARK
             ├─ explicit official web help → ChatGPT web search + source URL
             └─ AgentOrchestrator → bounded plan / approve / execute / recapture
                                     ├─ UIA snapshot (read)
                                     └─ SendInput (selected task access policy; confirm each by default)
Replies → Home conversation + local SAPI voice + temporary click-through marks
```

| Project | Role |
| --- | --- |
| `OpenClicky.App` | Home and tray host, settings, human confirmation, local speech, secure storage |
| `OpenClicky.Core` | Shared turn pipeline, agent loop, POINT/MARK validation and mapping, app guides, settings, deny-list, text history |
| `OpenClicky.Capture` | WGC/D3D11 monitor capture with GDI fallback; in-memory JPEGs |
| `OpenClicky.Input` | Remappable global push-to-talk and Escape cancellation |
| `OpenClicky.Overlay` | Click-through companion, fly-to, labels, speech bubble |
| `OpenClicky.Providers` | ChatGPT OAuth, account-specific model discovery, and streamed Responses; optional OpenRouter vision/transcription |
| `OpenClicky.Agent` | UIA snapshots, focus restoration, SendInput tools |
| `OpenClicky.Sandbox` | Reversible action test window |
| `*.Smoke` | Separate capture, overlay, action, and secret-store/speech checks |
| `services/local-proxy` | Existing optional localhost provider sidecar |

Microphone choices use persistent native endpoint IDs rather than enumeration positions. WASAPI reads the selected endpoint; a disconnected explicit choice fails visibly. Buffered snapshots and the final capture are resampled in memory to 16 kHz mono PCM. English Whisper base.en supplies previews every few seconds and final recognition, with serialized inference and asynchronous native disposal after cancellation. A size/SHA-256 check precedes model loading; downloads are bounded and atomically replace only a verified file. Before model download, Windows compatibility recognition reads the same selected microphone's WAV, with words appearing after submission. Requests are bounded to one minute; silence is rejected by energy. A dedicated speech detector and multilingual/model choices remain future work.

Text and voice use the same pipeline. Whisper supplies live previews; releasing the shortcut or submitting the voice button completes recognition before sending. A new request cancels the old one; a semaphore prevents overlapping AI turns. Empty speech requests are visibly labeled screen help and never run Agent. The overlay hides synchronously before screenshots. Ask adds a detail capture of the active window's visible screen region so a small window on a high-resolution monitor remains legible. It shares the same visible pixels, stays in memory, and maps coordinates back to that region; it does not capture hidden content. A failed detail capture retains the monitor views. Agent's capture path is unchanged. Marks are bounded to six, checked against the selected image and mapped into virtual-screen coordinates. Marks remain for at least eight seconds during an uninterrupted reply, and clear on cancellation or a window change. A changed window while inference is pending discards the answer. Same-window navigation and arbitrary layout changes are not tracked continuously.

The optional app-switch guide is off by default, observes window titles, waits two seconds for a stable supported app, and makes at most one request every ten seconds while idle. Escape disables it. Title hints are untrusted convenience data. The active window's bounds are clipped and mapped into each monitor image to distinguish neighboring windows. Screen orientation and official help exclude old task history; ordinary follow-ups retain it. Screenshot-visible controls take precedence over bundled documentation. No private ChatGPT or Claude chat database is imported. Explicit web help enables an official-domain web search tool; routine questions do not. Prompts restrict search terms to public app/feature names, but this is model behavior rather than a data-loss prevention boundary. ChatGPT Ask can select a separate quick model from the account catalog; Agent retains the main model.

The shared Agent loop defaults to eight steps; native workspace tasks are capped at twenty-four. Invalid tools or coordinates are rejected. Decline or failed execution stops the run. Changed foreground windows or explicit steering discard the old plan before input and require fresh grounding. The confirmation restores both the target window and its focused child control before input. The built-in sandbox is the only permitted in-process input target. Each subsequent step captures the desktop again; the planner is instructed to inspect the result before declaring success. This is still model behavior, not a general correctness guarantee.

The 0.4 workspace keeps Chat, Tasks, Memory, Notes and Connections in one native window. Desktop tasks have their own cancellation and pause gate, so another voice turn can steer them without replacing the task. Bringing Home forward or beginning voice pauses input at the next action boundary. A task snapshots Preview, Confirm each, Auto or Full access when it starts. Auto approves a plan and uses conservative rationale/argument matching for further prompts; the heuristic can miss risk. Warned Full access skips individual native action confirmations. Window/title guards are soft safeguards, not security boundaries.

Explicit note, remember and task-control commands are handled before screen capture or a provider call. Memory contributes only its user-authored profile and latest twenty facts to future prompts; Notes are not mined automatically. Both use bounded atomic local stores. Task history records bounded narration/progress and can export AG-UI NDJSON, but no live CopilotKit transport is built. Selected-area questions capture and share only the returned rectangle. The companion's circular marker and bounded fading trail stay on a non-activating overlay.

OpenCodeTaskClient is an isolated, bounded loopback HTTP adapter verified against installed OpenCode 1.17.18. The workspace currently exposes a health check; real session handoff, provider consent and cross-engine permissions remain separate work. It does not copy ChatGPT credentials. Composio/Nango account integration remains paused by the user. See [the integration design](unified-companion-integration.md) for the researched source and license boundaries.

Settings and text history are written through a temporary file and replaced atomically. History is capped at ten Talk turns. Screenshots stay in memory and API keys are excluded from settings JSON. Saved history is plaintext, optional, and enabled by default. See [agent safety](agent-safety.md) for the distinction between title guards, human approval, and security boundaries.
