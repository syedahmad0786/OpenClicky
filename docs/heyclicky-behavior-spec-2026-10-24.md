# HeyClicky behavior spec and OpenClicky phase-two gap — 24 October 2026

Clean-room input for OpenClicky phase two. This records **what the shipped HeyClicky for Windows 1.1.1 does**, in our own words, so OpenClicky can implement equivalent behavior independently. No HeyClicky code, prompt text, skill files, icons, keys or endpoints are copied into OpenClicky, and OpenClicky must never call `api.heyclicky.com` or HeyClicky's Supabase project. Extends [heyclicky-research.md](heyclicky-research.md) (marketing/video evidence) with shipped-artifact evidence.

## Method and evidence boundary

- Target: `%LOCALAPPDATA%\HeyClickyApp\current`, Velopack package `HeyClickyApp` 1.1.1, `win-x64`. Nothing was launched.
- Tool: `rea-agents@6.5.0` CLI (`inspect-managed-artifact`, `inspect-managed-members`, `inspect-managed-native-boundaries`, `analyze-javascript-application`). Static metadata only: type/member names, signatures, string literals, P/Invoke imports.
- Evidence IDs: main `HeyClicky.dll` identity `ev_214ecc3d…` (members **not admitted**: metadata exceeds REA's 64 MB budget, `ev_89c91008…`); `HeyClicky.Agents.dll` `ev_d7e57f2b…`; `HeyClicky.Platform.dll` members `ev_67843814…`, native boundaries `ev_8b118a97…`; `HeyClicky.Contracts.dll` `ev_8b1679e6…`; `HeyClicky.Drawing.dll` `ev_9d82c31a…`; `HeyClicky.Drawing3D.dll` `ev_572df7ba…`; Chrome extension 0.6.3 `ev_b3d07b1a…`.
- `HeyClicky.dll` and `HeyClicky.Core.dll` were read only for type names and user strings with `System.Reflection.Metadata` (no code loaded). No method bodies were decompiled for any assembly.
- **Observed** = present in shipped metadata/strings. **Inferred** = behavior reasoned from names and strings; not seen executing. Runtime behavior, timing, and server-side logic are **unknown**.

## Architecture (observed)

| Layer | HeyClicky | OpenClicky today |
| --- | --- | --- |
| Shell | .NET 9 WinUI 3, Velopack updates, Sentry crash reports, PostHog analytics | .NET WinForms/WebView2 host, portable ZIP + unsigned MSIX |
| Voice in | Deepgram streaming (token minted by backend), separate dictation pipeline | Local Whisper base.en, WASAPI endpoint selection |
| Voice out / live talk | OpenAI Realtime (`gpt-realtime-*`) with backend-minted client secret | Streamed ChatGPT text + local SAPI speech |
| "Brain" turn | Streaming SSE turn with tool calls, computer-use calls, request compression and context compaction | `TalkOrchestrator` + ChatGPT Responses |
| Background agents | Bundled **Codex CLI app-server** (v0.152.1) over JSON-RPC, custom model provider routed through their backend; alternate Node "harness runner" on loopback | Bundled **OpenWork 0.18.57 / OpenCode 1.18.30** on loopback |
| Desktop control | `SendInput`, low-level hooks, raw input, UIA, clipboard, foreground control | `SendInput` + UIA with Preview/Confirm-each/Auto/Full access |
| Drawing | Direct2D overlay + Direct3D 3D figures | Seven 2D primitives + bounded CopilotKit 3D cube |
| Browser | MV3 extension with `debugger` (CDP) + native host `com.heyclicky.browser` | MV3 extension + native messaging, scoped (manual acceptance open) |
| Connectors | Composio-managed accounts, multiple accounts per toolkit, custom MCP servers with OAuth (dynamic client registration, loopback callback) | Researched, paused ([connector decision](connector-decision-2026-10-12.md)) |
| Accounts/billing | Supabase auth, Stripe plans, credit leases per agent turn | ChatGPT plan sign-in; no billing (not needed) |

## Phase-two behavior to reproduce

### 1. Named companions ("Clickys") — observed
- Each companion has a slug, display name, role text, one-liner, creation time, archive flag and a bound conversation thread.
- Each owns a **workspace folder** with a seeded instructions file (role + dated notes the agent maintains; secrets must never be written there) and a notes file the app can append to.
- Threads can be bound, unbound, hidden, retitled; standing instructions are carried into the first turn of a thread and fingerprinted so they are re-sent only when changed (inferred from "standing context ledger").
- Operations: create, rename, replace role, archive/unarchive, delete all on sign-out.

### 2. Background jobs and work shelf — observed
- A **job** = id, title, prompt, thread, folder, created/finished time, outcome, summary, reply text, artifact paths, seen/dismissed/archived flags, colour tag, and an optional "waiting on the user" step.
- Each job has a folder: deliverables in `output/`, scratch in `tmp/`, plus an app-maintained index (newest-first list of work pieces and a runs block).
- Runs append to a job; follow-ups route either into the live thread or a new run that resumes a finished one.
- The agent's final reply is parsed into a **structured result**: plain text, next actions, done title, artifact list, summary, optional routine request, optional computer-use request, optional "user step" (instruction + page + `done_when_url_contains` for the browser watcher), and learned how-tos.
- Live progress: plan steps with status, streamed commentary, a "thinking" heading, activity rows, and short spoken progress lines.
- **Learned how-tos** are saved per job and injected into later prompts.
- A step watcher observes the frontmost browser address to detect when a user-performed step is done (inferred).
- Restart interruptions are recorded and offered back in chat.

### 3. Routines (scheduled runs) — observed
- Routine = owner companion and/or job, title, task prompt, schedule (minute of day + one or many weekdays, or fixed interval), pause flag, next run, run count, last start/finish/summary/failed, pending retry, consecutive-failure tracking.
- Scheduler: evaluates due routines, skips when the owner is busy, holds runs until a **full wake** (display on, not standby), waits for network, retries with backoff, reconciles unfinished runs after restart, and keeps the PC awake on AC power while a run is in flight (`PowerCreateRequest`/`PowerSetRequest`).
- The agent can create/run/rename/pause routines through a tool (`action`, `title`, `task`, `every_minutes`, `new_title`), and approved proactive suggestions can become routines.
- Analytics events exist for created/updated/deleted/paused/dispatched/ended (OpenClicky: local history only, no analytics).

### 4. Agent runtime contract — observed
- One local agent server process per app, killed with the app via a Windows Job Object.
- Per-turn context: instructions, MCP servers (name, URL, bearer token, headers, whether turn identity is sent), skill directories, note path, plan-tool use.
- Turn operations: run, steer (inject a message mid-turn), interrupt, prewarm; concurrent task cap; auto context compaction at a token limit; reasoning effort setting.
- A loopback MCP endpoint exposes the app's own capabilities (browser, computer use, notes, routines) to the agent with a per-launch bearer token from an environment variable.
- Skills are synced into the agent home from bundled resources plus per-integration skill files.

### 5. Connectors — observed
- Catalog of integrations: id, name, description, status, connected/total counts, toolkits (slug, name, logo, connected), accounts (id, name, label, avatar, connected-at) and use cases; fuzzy search with recommended ranking.
- Connect / disconnect / label account; multiple accounts per toolkit with explicit per-task selection lines.
- **Custom MCP connectors**: OAuth discovery, dynamic client registration, loopback callback server, token storage, tool-catalog probe and verification before enabling.

### 6. Proactive layer — observed (type names only; behavior inferred)
- Morning greeting and morning suggestion cards with invitation/dismissal history and per-day slots.
- Foreground-activity signals → integration suggestions ("you use X; connect it?") with cadence rules and lane pauses.
- Upcoming-meeting countdowns from the calendar connector.
- Suggestion approval can spawn a new companion or routine.

### 7. Browser agent — observed
- Extension drives a tab through CDP: `Runtime.evaluate`, `Input.dispatchMouseEvent/KeyEvent/insertText`, `Accessibility.getFullAXTree`, `Page.captureScreenshot`, `DOM.setFileInputFiles`, dialog handling, network lifecycle; operations `snapshot`, `click`, `click_at`, `type`, `scroll`, `screenshot`, `wait_for`; tab grouping for agent work.

### Not reproduced (deliberately)
Billing/credit leases, Stripe, referral, Supabase accounts, PostHog/Sentry telemetry, HeyClicky's hosted worker and model proxy, their prompts and skill text.

## Gap analysis

| # | Capability | OpenClicky state (from [feature plan](feature-plan.md)) | Gap |
| --- | --- | --- | --- |
| G1 | Named companions with own workspace + notes | One companion | **Missing** |
| G2 | Jobs with folders, `output/`, structured results, work shelf, follow-ups | Task history + receipts in one timeline | **Partial**: no per-job folder/artifact model or structured final reply |
| G3 | Routines + scheduler (wake/network/retry/keep-awake) | None | **Missing** |
| G4 | Agent steer/interrupt/prewarm, concurrency cap, app MCP endpoint, skill sync | OpenWork runner + `McpDesktopBridge` | **Partial**: verify steer/interrupt and multi-task |
| G5 | Connectors (managed accounts + custom MCP OAuth) | Paused | **Missing**; follow connector decision (user-owned key, read-only first) |
| G6 | Proactive suggestions | Local awareness offers | **Partial** |
| G7 | Browser CDP agent | Scoped extension | **Partial**: compare op set; acceptance open |
| G8 | Realtime voice | Local Whisper + SAPI | **Different by design**; optional cloud realtime later |

## Recommended build order

1. G1 + G2 together (one data model: companion → jobs → runs → artifacts), on the existing OpenWork runtime and task timeline.
2. G3 routines on top of jobs, local-only scheduler.
3. G4 hardening as needed by 1–3.
4. G5 one read-only connector per the existing decision, then custom MCP OAuth.
5. G6, G7, G8 after the above are accepted.

Each step keeps OpenClicky's evidence levels: built → native accepted → physically accepted.
