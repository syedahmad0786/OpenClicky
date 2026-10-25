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
| F1 | Integrations page | Search, Connected / Available sections, logos, status chips (Connected, Partial, Reconnect required, Unavailable, Error), Connect/Configure/Disconnect/Reconnect | Built — Integrations page with search, Connected and Available sections, status chips (Connected, Checking, Needs sign-in, Reconnect required, Key rejected, Error), Connect/Reconnect/Remove, and a "what leaves this PC" notice; statuses come from a real `initialize` + `tools/list` when the page opens; native look check pending. Evidence: `ConnectorStatusRulesTests`, `ConnectorCatalogTests`, `ShellContractTests`, `ShellSettingsBuilderTests`, vitest `integrations.test.tsx`, `contract.test.ts`; SecretStore.Smoke `--connectors` (commits a35e466..b304459) | S+L |
| F2 | Multi-account | Several labelled accounts per app (Work/Personal/Other), "add another account", expired → Reconnect; per-task account choice rules | Built — up to 5 labelled accounts per app (Work, Personal, Other…), "Add another account", own sign-in, Reconnect and Remove per row, a renamable label, and the account label shown to the agent; native check pending. Evidence: `ConnectorRegistryTests`, `ConnectorFieldsTests`, `ConnectorRequestTests`, `ShellSettingsBuilderTests`, vitest `integrations.test.tsx` | S |
| F3 | Managed catalog | Server catalog via Composio | Built — a built-in list of public MCP servers (sign-in, token and no-account apps) plus the user's own Composio MCP address and key; never HeyClicky's backend (see F-S1); native check pending. Evidence: `ConnectorCatalogTests`, `ConnectorRequestTests`, vitest `integrations.test.tsx` | S |
| F4 | Custom MCP | Remote URL or local command; auth Sign in with browser / API key / None; advanced client ID, headers, env (encrypted); OAuth discovery + dynamic registration + PKCE + resource + loopback callback + refresh; verify via initialize + tools/list; 9 sign-in states | Built — custom remote URL or local command over stdio; browser sign-in (discovery, dynamic client registration, PKCE S256, `resource`, loopback callback, refresh with "Reconnect required"), API key or none; extra headers and environment variables kept encrypted; checked by `initialize` + `tools/list`; native check against a real account pending. Evidence: `RemoteMcpConnectorTests`, `StdioMcpConnectorTests`, `ConnectorVerifierTests`, `McpOAuthTests`, `McpTokenSourceTests`, `ConnectorFieldsTests`; SecretStore.Smoke `--connectors` ran sign-in, verify, forced refresh, refused refresh, a real secret-store round-trip and a powershell.exe stdio server on loopback (all PASS) | S |
| F5 | Obsidian | Local vault folder as a skill, no account | Built — a local vault folder (must hold `.obsidian`) as an in-process connector with list, search, read and write tools through the same access rules; escapes, hidden folders and junctions refused; native check pending. Evidence: `ObsidianVaultConnectorTests`, `ConnectorRegistryTests`; SecretStore.Smoke `--connectors` (vault list, search, read, five refused escapes, one valid write) | S |
| F-S1 | HeyClicky's managed catalog through its backend and Composio broker (`/agent/integrations…`, Composio session) | Server catalog via Composio | Skip — never call HeyClicky's backend. Users bring their own Composio MCP URL and key, or connect a public server directly (F3) | — |
| F-S2 | Account name, avatar and "Organization" flag on account rows | Shows who is signed in | Skip — generic MCP has no identity call. Rows show the label, host and "Added" date instead | — |
| F-S3 | Editing a saved custom connector | Edit in place | Skip — secrets are write-only by design: remove and add again. Labels can be renamed | — |
| F-S4 | Runtime-side sign-in (Codex `mcpServer/oauth/login`) | The runtime signs in to the server | Skip — OpenClicky proxies every connector itself, so app-side OAuth is the only path | — |
| F-S5 | One-line capability description derived from tool names | Short summary under each app | Skip — agents call `connector_tools` for the real list; add it only if the tool description gets too long | — |
| F-S6 | Logo images per app | Logos on every row | Skip — no logos are bundled or fetched; rows use the name, with no third-party art | — |
| F-S7 | Integration nudges | Suggests connecting an app during a task | Skip here — belongs to sub-project P (spec §P) | — |
| G1 | Browser card | Chrome/Brave/Edge/Arc/Vivaldi detection, profile picker, Install for me / I'll install it, Connected/Working, Disconnect | Partial (setup form) | S |
| G2 | Browser agent | MV3 extension using the debugging protocol: 24 operations (tabs, open, close, snapshot, extract, click, fill, type, press, scroll, wait_for, back, screenshot, click_at, hover_at, drag_at, javascript, upload, download, dialog, keep, batch …); page diffs; stable element refs; refuses password typing | Partial (observe/click/type/follow-link via native messaging) | S |
| G3 | Tab groups | One coloured group per Clicky in a shared work window; user tabs claimed only when changed and handed back; unkept tabs close at turn end | Missing | S |

**Native-accepted: pending.** F1–F5 move to Native-accepted after the user runs these nine checks (they need a real account, browser and eyes). The automated loopback run (`OpenClicky.App.SecretStore.Smoke.exe --connectors`) already passed; it uses a fake server and a fake browser step, so it proves our side of the protocol, not a real provider:

1. Settings → Integrations: Connected / Available sections, search, and the "what leaves this PC" notice look right in dark and light themes.
2. Connect Linear (or Notion): the browser opens the provider's own sign-in; after signing in, the row shows "Connected" with a tool count.
3. "Add another … account", label it Personal, sign in with a second account: two rows, labels shown; an agent task "list my Linear issues" says which account each came from.
4. Add GitHub with a personal access token: Connected; paste a wrong token: "Key rejected".
5. Add DeepWiki (no account): Connected.
6. Add a local command (for example `npx -y @modelcontextprotocol/server-everything`): Connected with its tools.
7. Set up Composio with your own MCP address and key: Connected.
8. Choose an Obsidian vault; ask an agent to find a note; ask it to create a note: Confirm-each asks before writing.
9. Revoke the OpenClicky app in a provider's settings, then open Integrations: "Reconnect required"; Reconnect fixes it.

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
