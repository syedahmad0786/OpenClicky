# HeyClicky 1.1.1 → OpenClicky feature ledger — 24 October 2026

Every HeyClicky for Windows 1.1.1 feature we could find, what OpenClicky has today, and how each one gets reverse-engineered, rebuilt and checked. Clean-room: this ledger and the inventories describe behaviour in our own words. OpenClicky never copies HeyClicky code, prompt text, art, sounds, fonts or wording, and never calls HeyClicky's backend, its Supabase project, or Stripe.

Sources (all in [`docs/research/heyclicky-1.1.1/`](../research/heyclicky-1.1.1/)):
[ui-inventory](../research/heyclicky-1.1.1/ui-inventory.md) · [voice-inventory](../research/heyclicky-1.1.1/voice-inventory.md) · [agents-inventory](../research/heyclicky-1.1.1/agents-inventory.md) · [connect-inventory](../research/heyclicky-1.1.1/connect-inventory.md) · [live-ui-notes](../research/heyclicky-1.1.1/live-ui-notes.md) (view-only look at the running app, user signed in) · earlier [behaviour spec](../heyclicky-behavior-spec-2026-10-24.md).

Status: **Have** = OpenClicky already does it · **Partial** · **Missing** · **Skip** = deliberately not reproduced · **Extra** = OpenClicky-only, kept.
RE depth: **S** = static evidence is enough · **L** = needs a live view-only look at HeyClicky for look/timing · **B** = behaviour needs a hands-on trial in HeyClicky by the user.

## 1. Shell and look (sub-project A)

| ID | Feature | HeyClicky behaviour (summary) | OpenClicky today | RE |
|---|---|---|---|---|
| A1 | Notch | Dark pill top-centre; hover/activation expands a tabbed peek (Home · Chat · Settings · transient Document); follows the display under the cursor; never takes focus; files can be dropped on it | Missing (normal Home window + round dock) | L |
| A2 | Home tab | Time-of-day greeting with name; hold-keys hint; jobs working/need-you line; featured job result card; connected-apps pill; (referral pill = Skip) | Missing | S+L |
| A3 | Chat tab | History rows (text, hand-off, drawing, action captions, date separators), composer with attachments/drop/paste, talk button, earlier/newest controls, full-reply view, read-aloud switch, empty/error states | Partial (Chat tab + React chat) | S+L |
| A4 | Document tab | Viewer for Markdown, spreadsheet (sum/avg/count), PDF first pages, text; open in app / show in Explorer / close | Missing | S |
| A5 | Notch cards | ~20 transient cards under the notch: activity strip, text input/response with auto-dismiss, job update/needs-you, muted speaker, mic trouble, dictation clipboard, dictionary learned, document access, app updated, tips | Missing (balloon tips) | S |
| A6 | Agents dock | Paper-note job cards fanned at the right screen edge / carousel either side of the notch; states Queued/Working/NeedsYou/Done/Failed/Stopped/New; plan checklist, live line, progress, file chips, ready-output button, footer requests, "+N more" | Missing (Work tab lists) | S+L |
| A7 | Visual system | Dark near-black panels, rounded cards, white pill secondary / blue gradient primary buttons, green status badges, small-caps section headers; paper/ink job cards; accent = cursor colour (blue #3380FF default, red, yellow, green); spring motion with reduced-motion fallback; follows Windows light/dark | Missing (light green WinForms theme) | L |
| A8 | Tray menu | Open Settings · Pause/Resume · Report a bug · Request a feature · What's new · Check for updates · Quit | Partial (different items) | S |
| A9 | General behaviour | Show in taskbar; show in screen recordings (capture exclusion); launch at login; pause (ignore shortcuts) | Missing | S |
| A10 | Updates | Hourly check, "Install & Relaunch", install on quit, "What's new" | Missing | S |
| A11 | Onboarding | Intro (welcome, colour pick), permission cards (mic, screen), hands-on tour (mic check, voice hello, draw demo, circle-to-ask, drive demo, text mode, dictation, finale) | Missing | S+L |
| A12 | Accounts/plans/paywall/referral/telemetry | Google sign-in via Supabase, Stripe plans, usage leases, Invite & Earn, PostHog, Sentry | Skip — OpenClicky keeps ChatGPT sign-in, no billing, no telemetry | — |

## 2. Voice, talk and companion (sub-projects B, C)

| ID | Feature | HeyClicky behaviour | OpenClicky today | RE |
|---|---|---|---|---|
| B1 | Hotkeys | Hold Ctrl+Alt to talk; double-tap Ctrl for text mode; hold Ctrl+Win to dictate; double-tap Ctrl+Win hands-free dictation; modifier-only chords; recorder (tap ≤0.4 s, double-tap ≤0.5 s, 7 s timeout, conflict error); Start-menu masking; Esc cancels | Partial (one configurable chord, Win+Alt+Space) | S |
| B2 | Text mode | Small input card under the notch, streamed answer with copy/dismiss/auto-dismiss/follow-up | Partial (Home composer) | S |
| B3 | Speech-to-text | Cloud streaming, pre-warmed on key-down, batch fallback, vocabulary keyterms | Have (local Whisper) — keep local default | S |
| B4 | Voice out | 10 named voices with previews, speed 0.5–1.5x, line-by-line queue with gaps, warm connection, text fallback | Partial (Windows SAPI voice) | S |
| B5 | Captions | Word-by-word captions at screen bottom while it talks | Missing | S+L |
| B6 | Devices | Mic list + test meter + recover/fallback; output device list + test sound; muted-speaker card (copies reply to clipboard) | Partial (mic list + test) | S |
| B7 | Brain turn | Screenshot of every display per turn, settle wait, self-overlay excluded; history caps; compaction; ≤5 tool rounds; reply mixes spoken lines and drawing calls | Built — every display, own windows excluded; native check pending (POINT/MARK parsing already there) | S |
| B8 | Talk tools | open URL, copy, insert text, capture screen, shell/file tools, web search, memory, calendar, Spotify, spawn/steer/status/interrupt agents, artifacts, routines, show result | Partial (agent mode + official web help) | S |
| B9 | Memory | Save/recall memory; server-side user layer | Have (local memory) — Extra kept | S |
| C1 | Companion cursor | Rounded glowing triangle in the accent colour following the pointer on a spring; listening waveform, thinking dots, word-by-word message bubble, label bubble; dock into notch; hide; shake to detach and click to talk | Partial (mint face buddy + round dock) | L |
| C2 | Pointing | Fly to target, dashed glowing click ring with label and pulse | Have (fly-to + label) — restyle | S |
| C3 | Drawing engine | Primitives line/arrow/curve/poly/path/circle/rect/dot/image/text/highlight/graph/timeline/figure/scene3d; helpers point/target/frame/ring/trace/scroll/spotlight/button/sticky/photo/card/tie; live edits; timed to speech | Partial (7 shapes) | S |
| C4 | 3D figures | Direct3D figures (box, sphere, cylinder, cone, torus, capsule, tube, lathe, extrude, gear, spring …) on screen | Partial (WebView cube/pyramid) | S |
| C5 | Screen highlight | Draw on screen while holding talk keys; strokes classified (circle/line/scribble) and the UI text under them sent with the question | Partial (drag-rectangle region ask) | S |
| C6 | Walkthroughs | Box snaps to the real UIA control; watches the user's click to advance; ≤15 steps; hover targets complete after 2 s | Partial (Next/Back/Recheck window) | S |
| C7 | Saved drawings | Save/clear offer, replay in window/full-screen/board, record to video, print, keep in chat | Missing | S |
| C8 | Drive (computer use) | Offer + consent card; holds mouse/keys while driving; Esc pill; mouse move ~40 px takes back; elevated-window detection | Partial (access modes + confirm dialogs) — keep OpenClicky safety rules | S |

## 3. Dictation (sub-project D)

| ID | Feature | HeyClicky behaviour | OpenClicky today | RE |
|---|---|---|---|---|
| D1 | Hold/hands-free dictation | Hold Ctrl+Win, release inserts at caret; double-tap for hands-free | Built — hold and double-tap hands-free with Stop and activity card; native check pending the user's list below. Evidence: `DictationGateTests`, `DictationCardsTests`, `DictationFlowTests`; SecretStore.Smoke `--automatic-dictation`, `--dictation-ui-state` (commits 2d9dc1e..3463e6b) | S |
| D2 | Context + cleanup | Reads focused field over UIA (app, site, nearby text) to clean up the transcript | Built — field context read over UIA (≤2,000 chars before/after/selected, passwords and own windows never read) and AI tidy-up with a result guard and 6 s timeout; native check pending. Evidence: `DictationFieldContextTests`, `DictationCleanupTests`, `DictationTextFitTests`; Agent.Smoke `--dictation` (`dictation_context`, `dictation_flow`) | S |
| D3 | Insertion | Paste + verify + clipboard restore; per-app strategies; refuses passwords/address bars; clipboard fallback card | Built — keeps the verified Unicode `SendInput` + UIA read-back, refuses passwords; a failed or unverifiable insert goes to the clipboard with a card; native check pending. Evidence: `DictationFlowTests`; Agent.Smoke `--dictation` fixture check (real control read back as `Say: Hello, world.`; real clipboard read back, user's clipboard restored) | S |
| D4 | Dictionary | ≤50 custom words; learns single-word corrections after insertion; "added to dictionary" card | Built — ≤50 words of ≤60 characters in the Whisper prompt and tidy-up; single-word corrections learned with an Undo card; native check pending. Evidence: `DictationCorrectionDetectorTests`, `LevenshteinTests`, `WhisperPromptTests`, `SettingsCatalogTests` | S |
| D5 | Languages | ~50 languages + auto-detect | Built — 50 language codes plus auto-detect and an optional multilingual Whisper model (size and SHA-256 verified, downloaded only on click); native check pending. Evidence: `WhisperModelChoiceTests`, `SettingsPatchTests`, vitest `voicePages.test.tsx` | S |
| D-S1 | Paste-based insertion (clipboard snapshot/restore) and blind paste for apps without UIA | Pastes with Ctrl+V and restores the clipboard; pastes blind where UIA gives nothing | Skip — we keep the verified insertion, which never touches the clipboard and can be checked; fields it cannot insert into get the clipboard card (D3) instead of an unverified blind paste | — |
| D-S2 | Terminal-specific insertion | Per-terminal strategies | Skip — terminals expose no writable `ValuePattern`, so they take the clipboard path; revisit if users ask | — |
| D-S3 | Streaming (cloud) STT, warm sockets, batch upload | Cloud speech-to-text | Skip — dictation stays on local Whisper; speech never leaves the PC | — |
| D-S4 | 14-day local transcript archive | Keeps dictated transcripts for 14 days | Skip — not in the spec; it would keep a store of everything dictated; add only if a history UI is asked for | — |
| D-S5 | Word limits / paywall | Weekly word cap tied to plans | Skip — OpenClicky has no plans or limits | — |
| D-S6 | Seeding the account name and per-session context keyterms into the vocabulary | Adds the signed-in name and session terms to the vocabulary | Skip — the dictionary covers names the user cares about, and field context already reaches the tidy-up | — |

**Native-accepted: pending.** D1–D5 move to Native-accepted after the user runs these seven checks (they need a real voice and keyboard):

1. Hold Ctrl+Win in Notepad and say "um so the meeting is on Tuesday". Release: "So the meeting is on Tuesday." is typed at the caret (tidy-up on).
2. Double-tap Ctrl+Win, say two sentences with a pause, then double-tap again: both are typed, and the notch shows "Hands-free dictation" with Stop.
3. While holding Ctrl+Win, press another key (for example Ctrl+Win+D): dictation cancels and nothing is typed.
4. Dictate with the desktop focused: the clipboard card appears, and Ctrl+V in Notepad pastes the words.
5. Dictate a name Whisper gets wrong, fix that one word by hand: "New dictionary word" appears; Undo removes it from Settings → Dictation.
6. Download the multilingual voice, pick Spanish (or leave auto-detect on) and dictate a Spanish sentence.
7. Dictate into a password box: refused, nothing is typed or copied.

## 4. Background work (sub-project E)

| ID | Feature | HeyClicky behaviour | OpenClicky today | RE |
|---|---|---|---|---|
| E1 | Clickys (companions) | Named agents with their own memory/workspace, roster with portraits, starter cast | Have (companions, `AGENTS.md`, per-companion job folders) — the roster UI with portraits and a starter cast is a Skip (E-S6); the classic Work panel keeps creating companions | S |
| E2 | Jobs | ≤80 jobs, colour tags, rail phases, `output/` `tmp/` `note.md`, `WORK.md` index, runs block, 7-day restart interruption memory | Built — ≤80 jobs (oldest finished ones without a routine trimmed), least-used colour tags, queued/needs-you states, a job folder with `output/` `tmp/` `note.md` `runs.md`, `WORK.md` with a job list and a 50-run block that never drops the user's own text, and restart recovery (Stopped, "Interrupted when OpenClicky closed"; needs-you steps older than 7 days stop); native check pending. Evidence: `JobColoursTests`, `WorkStoreTests`, `WorkIndexTests`, `FileCarryOverTests`, `WorkNativeAcceptanceTests` (store check: 3 jobs, structured replies, the real `WORK.md` read back from a temp folder) (commits 6ed26a1..2e719bc (plan 9eb52e1)) | S |
| E3 | Structured result | Final reply parsed into summary, next actions, done title, artifacts, routine request, computer-use request, user step (wait-for URL), learned how-tos, answer | Built — the last fenced result block gives title, summary, ≤4 points, a stat, ≤3 next steps, files (accepted only inside the job folder), a routine request, a your-turn step and learned how-tos; the job card shows them. The computer-use request block and rich answer cards are Skips (E-S1, E-S2); native check pending. Evidence: `JobResultTests`, `JobCardsTests`, `ShellStateBuilderTests`, `WorkNativeAcceptanceTests` (shell check: real `ShellStateBuilder` card compared to `tests/fixtures/shell-job-card.work.v1.json`), vitest `work-card.acceptance.test.tsx` renders it (commits 6ed26a1..2e719bc (plan 9eb52e1)) | S |
| E4 | Live progress | Plan checklist, live step phrases, commentary, thinking heading, spoken start/finish announcements, update bubble beside cursor; quiet during calls/screen share/DND | Built — live plan checklist and step phrases per job, spoken start/finish lines, cursor updates beside the cursor, and quiet during calls and presentations (no sound or bubble; the card still appears); native check pending. Evidence: `JobProgressTests`, `JobAnnouncerTests`, `QuietStateTests`, `JobCardsTests`; vitest `notch.test.tsx`, `JobDeck.test.tsx` (commits 6ed26a1..2e719bc (plan 9eb52e1)) | S |
| E5 | Follow-up / steer / interrupt / prewarm | Steer mid-turn, interrupt, follow-up resumes a finished job, prewarm on key-down; 5 concurrent jobs | Built — up to 5 concurrent jobs, one desktop lease so only one run uses the screen and input (others wait with "Waiting for the screen"), steer a running job, follow-up and Try again from the card; Esc stops the run holding the lease. Prewarm and resuming a finished job's engine session are Skips (E-S4, E-S5); native check pending. Evidence: `JobAdmissionTests`, `DesktopLeaseTests`, `JobSteerTests`, `McpDesktopBridgeTests`; vitest `notch.test.tsx` (steer, follow-up and Try again intents) (commits 6ed26a1..2e719bc (plan 9eb52e1)) | S |
| E6 | User step watcher | "Your turn" step with a page; browser address checked every 2.5 s up to 6 h to detect done | Built — a "Your turn" card with Open page (http/https only, only on click); the browser address is read locally every 2.5 s for up to 6 h, or the user presses "I'm done"; cards survive a restart; native check pending. Evidence: `UserStepWatchTests`, `JobResultTests` (commits 6ed26a1..2e719bc (plan 9eb52e1)) | S |
| E7 | Learned how-tos | ≤60 saved; newest 10 + 5 most related added to prompts | Built — ≤60 how-tos of ≤240 characters in `howtos.json`; the newest 10 plus the 5 most related go into a job prompt (marked as untrusted notes). A how-to list/edit UI is a Skip (E-S8); native check pending. Evidence: `HowTosTests`, `FileCarryOverTests` (commits 6ed26a1..2e719bc (plan 9eb52e1)) | S |
| E8 | Routines | Voice-created only (routines tool: create/update/pause/resume/delete/run_now/status); Settings list with Run now/Pause/Delete and status; 30 s scheduler, stale 2 h, 3 min after wake, network wait, AC keep-awake, retry 300 s, pause after 3 failures | Built — a 30 s scheduler (stale after 2 h, held 3 min after wake, waits for the network, keeps the PC awake on AC while a routine runs, retries after 300 s, pauses after 3 failures in a row); "Schedule this?" confirm cards from voice or a job result (a routine is never created without Schedule); Settings → Routines shows status with Run now, Pause/Resume and Delete. Editing routines by voice is a Skip (E-S9); native check pending. Evidence: `RoutineSchedulerTests`, `RoutineRequestsTests`, `JobResultTests`; vitest `workPages.test.tsx` (Routines), `NotchCards.test.tsx` (commits 6ed26a1..2e719bc (plan 9eb52e1)) | S |
| E9 | Agent settings | Agent folder picker; auto-approve extra usage (→ Skip, no billing); speak start/finish; updates beside cursor; "what agents can reach" (Windows privacy states, opens ms-settings) | Built — Settings → Agents has the agent folder (Change/Reset; new jobs live there, and `WORK.md` and `howtos.json` are carried over once), speak start/finish and updates-beside-cursor toggles, and "What agents can reach" rows that open the matching Windows privacy page; extra-usage approval is a Skip (E-S3); native check pending. Evidence: `FileCarryOverTests`, `WorkStoreTests`, `JobAnnouncerTests`; vitest `workPages.test.tsx` (Agents) (commits 6ed26a1..2e719bc (plan 9eb52e1)) | S |
| E10 | Proactive | Morning paper, suggestion cards, integration nudges (10 s dwell, ≤3/24 h, 48 h snooze), meeting countdown (from 75 s) — paused in 1.1.1 | Partial (awareness offers) — not part of sub-project E; covered by sub-project P | S |
| E11 | Agent engine | Own H1 runner (loopback /turn /events /steer /cancel /prewarm), Codex and Pi options, 120 steps, 30 min | Built — keeps OpenWork/OpenCode and adds steer and 5-way concurrency under the desktop lease; engine prewarm and server pooling are a Skip (E-S4); native check pending. Evidence: `JobAdmissionTests`, `JobSteerTests`, `DesktopLeaseTests`, `OpenWorkServerServiceTests` (commits 6ed26a1..2e719bc (plan 9eb52e1)) | S |
| E-S1 | Rich answer cards (numbers with was/of/better, ranked lists, drafts, ask rows, "quiet") | A card grammar for answers: figures with before/after, ranked lists, drafts, questions with buttons | Skip — `stat` + `points` + `next` cover the common result shapes; the full card grammar is a large UI surface, revisit after side-by-side review | — |
| E-S2 | Computer-use request block ("one step on your PC") | The model asks to run one step on the PC and the user approves it | Skip — OpenWork jobs already carry the desktop tools under the user's access mode, with Confirm-each per action and the C12 drive consent; a separate request/approve round trip adds nothing | — |
| E-S3 | Extra-usage approval ("needs more agent messages") | Asks before spending more of a plan's agent messages | Skip — OpenClicky has no plans or billing | — |
| E-S4 | Engine prewarm (`/prewarm`) and server pooling | Warms the engine on key-down and reuses servers | Skip — each run starts its own OpenWork server (about 30 s budget); pooling is an engine performance change, revisit if start latency is a complaint | — |
| E-S5 | Resuming a finished job's engine session | A follow-up continues the same engine session | Skip — the server is disposed after each run; a follow-up starts a new run with the previous result, the job's `note.md` and `WORK.md` instead | — |
| E-S6 | Clicky roster UI with portraits and a starter cast | Roster of named agents with portraits and a starter cast | Skip — not in spec §E; the classic Work panel keeps creating companions, and the starter cast belongs with H (onboarding) | — |
| E-S7 | Chrome-extension wait states (held for Chrome, needs the extension) | A job waits while Chrome or its extension is unavailable | Skip — belongs to G (browser) | — |
| E-S8 | A how-to list/edit UI | Lets the user see and edit learned how-tos | Skip — not in the spec; how-tos are capped at 60 and age out, add a Settings list if users ask | — |
| E-S9 | Editing routines by voice (update/pause/resume/delete/status) | The routines tool can change and inspect routines | Skip — the Settings Routines list already offers Run now, Pause/Resume and Delete; creation is by voice (plan task E11) | — |
| E-S10 | Earcons for launch/done/needs-you | Short sounds for job events | Skip — we have no sound set of our own (same reason as C's skip) | — |

**Native-accepted: pending.** E2–E9 and E11 move to Native-accepted after the user runs these seven checks (they need a sign-in and real jobs):

1. Say "research the three best note-taking apps and save a comparison". A job card appears with a live checklist. When it finishes, the card shows the summary, points and the saved file; Open works.
2. Start two more jobs while the first runs. All three run, and a job that needs the screen waits with "Waiting for the screen".
3. Type into the job's box while it works: the job takes the update. After it's done, ask a follow-up; Try again on a stopped job works.
4. Ask for a job that needs a sign-in. The "Your turn" card appears; Open page opens the site; signing in (or I'm done) continues the job.
5. Say "every weekday at 8:30, summarise my unread email": the Schedule this? card appears; Schedule adds it to Settings → Routines; Run now works.
6. During a call (mic in use) or a presentation, a job finishing makes no sound and no cursor bubble, but the card appears.
7. Quit OpenClicky while a job runs, start it again: the job shows Stopped ("Interrupted when OpenClicky closed").

Automated acceptance (25 Oct, no sign-in): Release build with 0 warnings; Core 1,799, Input 48 and vitest 204 checks green; Overlay.Smoke, Agent.Smoke `--dictation` `--snap` `--harvest` `--integrity` and SecretStore.Smoke `--dictation-ui-state` `--automatic-dictation` pass. `WorkNativeAcceptanceTests` writes three jobs with structured replies into a temp store and reads the real `WORK.md` back (job list, run block, the user's own text outside the block surviving a re-render); it found and fixed a needs-you run printing as "working" in that block. Surface.Smoke cannot inject shell state, so the shell check is a vitest render of the real `ShellStateBuilder` card JSON.

## 5. Connectors and browser (sub-projects F, G)

| ID | Feature | HeyClicky behaviour | OpenClicky today | RE |
|---|---|---|---|---|
| F1 | Integrations page | Search, Connected / Available sections, logos, status chips (Connected, Partial, Reconnect required, Unavailable, Error), Connect/Configure/Disconnect/Reconnect | Partial (connector list) | S+L |
| F2 | Multi-account | Several labelled accounts per app (Work/Personal/Other), "add another account", expired → Reconnect; per-task account choice rules | Missing | S |
| F3 | Managed catalog | Server catalog via Composio | Skip HeyClicky's backend; offer the user's own Composio MCP URL/key + a built-in list of public MCP servers | S |
| F4 | Custom MCP | Remote URL or local command; auth Sign in with browser / API key / None; advanced client ID, headers, env (encrypted); OAuth discovery + dynamic registration + PKCE + resource + loopback callback + refresh; verify via initialize + tools/list; 9 sign-in states | Partial (remote URL + header token) | S |
| F5 | Obsidian | Local vault folder as a skill, no account | Missing | S |
| G1 | Browser card | Chrome/Brave/Edge/Arc/Vivaldi detection, profile picker, Install for me / I'll install it, Connected/Working, Disconnect | Partial (setup form) | S |
| G2 | Browser agent | MV3 extension using the debugging protocol: 24 operations (tabs, open, close, snapshot, extract, click, fill, type, press, scroll, wait_for, back, screenshot, click_at, hover_at, drag_at, javascript, upload, download, dialog, keep, batch …); page diffs; stable element refs; refuses password typing | Partial (observe/click/type/follow-link via native messaging) | S |
| G3 | Tab groups | One coloured group per Clicky in a shared work window; user tabs claimed only when changed and handed back; unkept tabs close at turn end | Missing | S |

## 6. OpenClicky extras kept (Extra)

Clipboard shelf · local Whisper and Windows voice (offline) · Preview / Confirm-each / Auto / Full access modes with deny list and physical-takeover pause · local notes · memory panel · interactive visuals (chart, diagram, calculator, 3D) · Tasks timeline with AG-UI export · awareness offers · OpenRouter option · sandbox demos. Each moves into the new shell (Settings sections or Chat/Home entries) rather than being removed.

## 7. Per-feature reverse-engineering and build loop

For every ledger row, in sub-project order:

1. **Understand** — re-read the row's inventory evidence. If RE = L, do a view-only look at the running HeyClicky (screenshots, no setting changes). If RE = B, ask the user to perform the action in HeyClicky and watch it.
2. **Specify** — write the behaviour in our own words (inputs, states, timings, edge cases) into the sub-project spec. No HeyClicky method bodies are decompiled; metadata, constants, strings and observed behaviour only.
3. **Build** — implement in OpenClicky following its existing patterns and safety rules.
4. **Test** — unit tests for logic (state machines, schedules, parsers, policies); native checks in the running app (inspect the real destination, not logs); side-by-side screenshot against HeyClicky for UI rows.
5. **Record** — mark the row Built → Native-accepted in this ledger and in `docs/feature-plan.md`.

## 8. Sub-projects and order

| # | Sub-project | Rows | Depends on |
|---|---|---|---|
| A | Shell, design system, notch, tray, general, updates | A1–A10 | — |
| S | Settings (all sections, new look) | A9, B1, B4, B6, C1, D4, D5, E8, E9, F1, F4, G1 | A |
| B | Hotkeys, text mode, voice out, captions, devices, brain turn | B1–B8 | A |
| C | Companion cursor, drawing engine, highlight, walkthroughs, drive | C1–C8 | A, B |
| D | Dictation | D1–D5 | B1 |
| E | Jobs, dock, structured results, routines, how-tos | E1–E9, E11 | A |
| F | Connectors | F1–F5 | S |
| G | Browser agent | G1–G3 | E |
| H | Onboarding | A11 | A–E |
| P | Proactive | E10 | E, F |
