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
| E1 | Clickys (companions) | Named agents with their own memory/workspace, roster with portraits, starter cast | Have (companions, AGENTS.md) — needs new UI | S |
| E2 | Jobs | ≤80 jobs, colour tags, rail phases, `output/` `tmp/` `note.md`, `WORK.md` index, runs block, 7-day restart interruption memory | Partial (jobs, output/, runs.md) | S |
| E3 | Structured result | Final reply parsed into summary, next actions, done title, artifacts, routine request, computer-use request, user step (wait-for URL), learned how-tos, answer | Missing | S |
| E4 | Live progress | Plan checklist, live step phrases, commentary, thinking heading, spoken start/finish announcements, update bubble beside cursor; quiet during calls/screen share/DND | Partial (Tasks timeline) | S |
| E5 | Follow-up / steer / interrupt / prewarm | Steer mid-turn, interrupt, follow-up resumes a finished job, prewarm on key-down; 5 concurrent jobs | Partial (one task at a time, follow-up) | S |
| E6 | User step watcher | "Your turn" step with a page; browser address checked every 2.5 s up to 6 h to detect done | Missing | S |
| E7 | Learned how-tos | ≤60 saved; newest 10 + 5 most related added to prompts | Missing | S |
| E8 | Routines | Voice-created only (routines tool: create/update/pause/resume/delete/run_now/status); Settings list with Run now/Pause/Delete and status; 30 s scheduler, stale 2 h, 3 min after wake, network wait, AC keep-awake, retry 300 s, pause after 3 failures | Partial (UI dialog, 60 s timer, retry 15/30/45) | S |
| E9 | Agent settings | Agent folder picker; auto-approve extra usage (→ Skip, no billing); speak start/finish; updates beside cursor; "what agents can reach" (Windows privacy states, opens ms-settings) | Missing | S |
| E10 | Proactive | Morning paper, suggestion cards, integration nudges (10 s dwell, ≤3/24 h, 48 h snooze), meeting countdown (from 75 s) — paused in 1.1.1 | Partial (awareness offers) | S |
| E11 | Agent engine | Own H1 runner (loopback /turn /events /steer /cancel /prewarm), Codex and Pi options, 120 steps, 30 min | Have (OpenWork/OpenCode) — keep, add steer/concurrency | S |

## 5. Connectors and browser (sub-projects F, G)

| ID | Feature | HeyClicky behaviour | OpenClicky today | RE |
|---|---|---|---|---|
| F1 | Integrations page | Search, Connected / Available sections, logos, status chips (Connected, Partial, Reconnect required, Unavailable, Error), Connect/Configure/Disconnect/Reconnect | Partial (connector list) | S+L |
| F2 | Multi-account | Several labelled accounts per app (Work/Personal/Other), "add another account", expired → Reconnect; per-task account choice rules | Missing | S |
| F3 | Managed catalog | Server catalog via Composio | Skip HeyClicky's backend; offer the user's own Composio MCP URL/key + a built-in list of public MCP servers | S |
| F4 | Custom MCP | Remote URL or local command; auth Sign in with browser / API key / None; advanced client ID, headers, env (encrypted); OAuth discovery + dynamic registration + PKCE + resource + loopback callback + refresh; verify via initialize + tools/list; 9 sign-in states | Partial (remote URL + header token) | S |
| F5 | Obsidian | Local vault folder as a skill, no account | Missing | S |
| G1 | Browser card | Chrome/Brave/Edge/Arc/Vivaldi detection, profile picker, Install for me / I'll install it, Connected/Working, Disconnect | Built — Settings → Integrations → Browser lists detected Chrome (stable/Beta/Dev/Canary), Chromium, Edge, Brave and Vivaldi with their profiles; Connect registers the native host per browser (HKCU only, on the click), opens the profile's extensions page, shows the extension folder and copies its path; Connected / Working / Disconnect states and a profile switch when several profiles connect; native check pending. Evidence: `BrowserCatalogTests`, `BrowserConnectionStoreTests`, `BrowserNativeHostRegistrarTests`, `BrowserSetupArgsTests`, `BrowserGateTests`, vitest `browserCard.test.tsx` (commits e7727f4..cd76b02) | S |
| G2 | Browser agent | MV3 extension using the debugging protocol: 24 operations (tabs, open, close, snapshot, extract, click, fill, type, press, scroll, wait_for, back, screenshot, click_at, hover_at, drag_at, javascript, upload, download, dialog, keep, batch …); page diffs; stable element refs; refuses password typing | Built — one `openclicky_browser` tool with the 24 operations over `chrome.debugger`; accessibility snapshots with stable refs (retired on navigation, never reused) and page diffs; password fields refused for `fill`, `type`, batch and `javascript`; `javascript` runs in an isolated world with cookies, storage and password values blocked; Confirm-each default, Preview read-only, Auto always asks for `javascript` and `upload`, decline ends the task's browser work; full-duplex native-host relay to a pipe-authenticated bridge server (up to 4 profiles, one selected); native check pending. Evidence: vitest `snapshot.test.mjs`, `page.test.mjs`, `inpage.test.mjs`, `core.test.mjs` (extension, 61 checks); `BrowserBridgeServerTests`, `BrowserOpsTests`, `BrowserUploadPolicyTests`, `ChromeBridgeSessionTests`, `BrowserNativeHostExeSmokeTests` (built exe → `ready{app:false}` → `ready{app:true}`); real-Chrome harness `apps/chrome-extension/test/cdp-smoke.mjs` (headless Chrome 155, throwaway profile, loopback CDP) — PASS: fixture server binds 127.0.0.1, DevTools endpoint is on 127.0.0.1, `netstat` shows the DevTools port listening only on 127.0.0.1 and owned by our Chrome, no non-loopback local address accepts a connection, navigate and settle, frame loading events fire, snapshot lists controls with refs, refs identical on a second snapshot, click returns a diff with the new line, fill and read-back (text, select, contenteditable), password fill and type refused with the exact refusal and the field stays empty, password text absent from snapshot and extract, covered click fails then succeeds with dismiss, extract has the table CSV and no field values, javascript returns the title and blocks `document.cookie`, `frames[0]` and `window[0]` cookie reads, storage and password reads, the alert blocks a click until `answerDialog`, screenshot is CSS-pixel sized (checked at device scale 2) and enables `click_at`, navigation retires refs, Enter before a snapshot is refused in a new epoch, back (commits e7727f4..cd76b02) | S |
| G3 | Tab groups | One coloured group per Clicky in a shared work window; user tabs claimed only when changed and handed back; unkept tabs close at turn end | Built — one coloured tab group per job (title and colour from the job) in a shared work window; reading a user tab leaves it in place, acting on it claims it and gives it back to its group and position at a tidy end; another job's tab is refused; Stop hands everything over and closes nothing; unkept tabs close when the job ends and the tab the user is viewing stays; native check pending. Evidence: vitest `tabs.test.mjs` (21 checks: `read leaves a user tab in place`, `claimed tab returns to its group and index`, `other job's tab is refused`, `hand over closes nothing`, `tidy end keeps the tab the user is viewing`), `core.test.mjs`; `ChromeBridgeSessionTests` (per-job browser sessions) (commits e7727f4..cd76b02) | S |
| G-S1 | "Install for me" (driving the install with SendInput, typing into chrome:// pages) | Installs the extension on the user's behalf | Skip — binding rule: the user installs any extension themselves; install help explains the steps, opens the extensions page and the folder, and copies the path | — |
| G-S2 | Chrome Web Store listing and its install path | Installs from the Store | Skip — OpenClicky has no Store listing; the bundled unpacked extension is the only build | — |
| G-S3 | Arc browser | Arc detection and install | Skip — store-packaged, and its native-messaging registration is unverified on Windows; a known gap (Chrome, Chrome Beta/Dev/Canary, Chromium, Edge, Brave and Vivaldi are supported) | — |
| G-S4 | Presence overlay in the page (edge glow, gliding cursor, speech bubble) | Draws a glow, cursor and bubble inside every page | Skip — cosmetic and draws inside every page; the job's progress line shows in the extension popup instead | — |
| G-S5 | Welcome page on install | Opens a welcome page after install | Skip — the browser card already shows the steps and the live state; the popup shows status | — |
| G-S6 | Per-turn analytics (`BrowserTurnUsage`) | Reports browser usage per turn | Skip — OpenClicky has no telemetry | — |
| G-S7 | "Ask once" setup offer and held jobs while Chrome is missing | Offers setup and holds jobs until a browser connects | Skip — agents only get the browser tool when the bridge is connected; without it they work without a browser and say so; revisit with E's "your turn" steps | — |
| G-S8 | Over-budget snapshots putting on-screen elements first | Orders snapshot lines by what is visible | Skip — snapshots are cut at the 12,000-character budget with a note to use `query` or `interactive` (a `ponytail:` comment marks the ceiling) | — |
| G-S9 | Exact-origin allow-list (old `bridge-config.json`) | Limits the agent to listed sites | Skip — replaced by the access modes plus a title/URL deny list (Ruling R1); the deny list is a convenience, not a security boundary | — |

**Native-accepted: pending.** G1–G3 move to Native-accepted after the user runs these ten checks (they need the user's real browser and signed-in sites). The harness above proved the page layer in a throwaway headless Chrome; it cannot load the extension (branded Chrome ignores `--load-extension`), show a tab group, or reach the user's profile:

1. Settings → Integrations → Browser: Chrome and its profiles are listed; pick a profile and click Connect. The extensions page opens in that profile and Explorer shows the extension folder. (Headless Chrome turns a `chrome://extensions/` command-line URL into `chrome://newtab/`, so only a real window shows whether the page opens.)
2. Turn on Developer mode, Load unpacked with that folder. The card turns "Connected" within a few seconds and the popup says "Connected to OpenClicky."
3. Ask a job to look something up on a site you're signed in to. A coloured tab group with the job's title appears next to your tab; the popup shows the job and its tabs; Chrome shows its debugging bar.
4. With Confirm-each, every click or form fill asks first; declining stops the job's browser work.
5. Ask a job to log in somewhere: it stops at the password field and asks you to type it.
6. Press Stop in the popup mid-job: the job stops and its tabs stay open.
7. Let a job finish: its unkept tabs close, a tab you were looking at stays, and any tab of yours it used goes back where it was.
8. Connect a second profile: the popup there says another profile is connected; switching in the card moves work to it.
9. Disconnect in the card mid-job: the popup says to finish setup at once, Chrome's debugging bar goes away and the job's tabs stay open; remove the extension yourself.
10. Quit OpenClicky mid-job: Chrome's debugging bar goes away and the tabs stay. Then, with OpenClicky closed, click Open OpenClicky in the popup: the app starts.

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
