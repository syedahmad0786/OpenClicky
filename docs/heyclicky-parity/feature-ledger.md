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
| E10 | Proactive | Morning paper, suggestion cards, integration nudges (10 s dwell, ≤3/24 h, 48 h snooze), meeting countdown (from 75 s) — paused in 1.1.1 | Built — brief, suggestions, connect nudges, meeting countdown; off by default; native check pending (rows P1–P4 below; sub-project P). | S |
| E11 | Agent engine | Own H1 runner (loopback /turn /events /steer /cancel /prewarm), Codex and Pi options, 120 steps, 30 min | Have (OpenWork/OpenCode) — keep, add steer/concurrency | S |

## 4b. Proactive (sub-project P)

All four features are **off by default** (Settings → Proactive); nothing is turned on by code, migration or test. Cards only appear in the notch shell. Proactive code only shows and removes cards; sending text, opening a link or opening Settings happens only when the user clicks a card button.

What leaves the machine: the brief sends nothing unless you click **Plan my day** (then its lines go to your model provider as a chat message together with the window in front (the same as any chat message)); suggestions send, once a day, up to 10 recent job titles, 10 routine titles, 20 connector names and 20 declined suggestion titles to your model provider, and **Do it** sends the shown message as a chat message together with the window in front (the same as any chat message); nudges send nothing (app name and window title are read on this PC only and never stored); the countdown asks the calendar connector you picked for events (that connector's own server), and event data does not go to the model provider unless you click **Plan my day**.

Quiet: cards wait during calls (microphone in use by another app), presentations, full-screen apps and Windows quiet hours as reported by the shell notification state, which may not reflect every Focus / Do Not Disturb mode; they come back when it ends (nudges do not). The undocumented quiet-hours state is not read, and E's quiet detection replaces this adapter at merge. Calendar: events are read through the connector you pick in Settings → Proactive, by a list-events tool the connector marks read-only, chosen by name, never a write tool; F's connectors replace this adapter at merge.

| ID | Feature | HeyClicky behaviour | OpenClicky today | RE |
|---|---|---|---|---|
| P1 | Morning brief | Morning paper card once a day with the day's plan | Built — off by default; one "Good morning" card per day (07:00–12:00, after real input in the last 30 s) built locally from calendar, jobs and routines; **Plan my day** sends it to chat; native check pending. Evidence: `MorningBriefTests`, `ProactiveCoordinatorTests` (brief cases); settings plumbing `SettingsPatchTests` (proactive cases), `ShellSettingsBuilderTests`, vitest `proactivePage.test.tsx` (commits 0df80a9..6f50071) | S |
| P2 | Suggestion cards | Daily suggestion cards from server-side generation | Built — off by default; once a day (09:00–20:00, retries 5/15/60/180 min) the user's own model provider proposes up to three suggestions, shown one at a time; **Do it** sends the shown message to chat, **Don't suggest** is remembered; native check pending. Evidence: `SuggestionPromptTests`, `SuggestionScheduleTests`, `ProactiveCoordinatorTests` (suggestion cases), `ProactiveStoreTests` | S |
| P3 | Connect nudges | Integration nudges (10 s dwell, ≤3 per 24 h, 48 h snooze, 18 s card) | Built — off by default; 20 popular apps recognised by app and window title on this PC, "Connect Slack?" style card with Connect (opens Integrations), Not now (just closes the card; the 48 h snooze applies once a nudge has been shown) and Never ask (stops nudges for that app for good); ≤3 per rolling 24 h, 48 h snooze; native check pending. Evidence: `NudgeCatalogTests`, `NudgeTrackerTests`, `ProactiveCoordinatorTests` (nudge case) | S |
| P4 | Meeting countdown | Meeting countdown from 75 s before start | Built — off by default; read-only events from the user's calendar connector, card from 75 s before start until 2 min after, text in 15 s steps, **Join** opens the meeting link; native check pending. Evidence: `MeetingCountdownTests`, `CalendarEventsTests`, `McpCalendarSourceTests`, `ProactiveCoordinatorTests` (countdown cases), `ProactiveHostInputsTests` | S |
| P-S1 | Server-side suggestion generation, job polling, receipts and server notifications | Suggestions generated on the HeyClicky server and polled | Skip — no HeyClicky backend; suggestions come from the user's own model provider, once a day, from local data Settings names | — |
| P-S2 | Three- and five-times-a-day cadences | More frequent suggestion slots | Skip — one daily slot (09:00–20:00) covers the feature; add cadences if asked | — |
| P-S3 | Approve-with-adjustment, founding a new Clicky, auto-creating a routine from a card | Richer card outcomes | Skip — **Do it** sends the suggested message to chat, where the user can ask for a job or routine | — |
| P-S4 | Separate morning-suggestions invitation card and its dismissal-rest rules | Invitation card with rest rules | Skip — paused in 1.1.1; the brief and suggestion cards cover it | — |
| P-S5 | Morning paper research job and random greeting lines/kaomoji | Research job plus randomised greetings | Skip — the brief is built locally from calendar, jobs and routines; one greeting in our own words | — |
| P-S6 | Long-tail nudge catalog (~70 apps), catalog refresh, Spotify 7.5 s dwell, post-connect verification | Large refreshable catalog | Skip — 20 popular apps; F owns connecting and verification | — |
| P-S7 | Directions action and spoken countdown or brief | Directions button, speech | Skip — cards only, Join/Open only; avoids speaking at the wrong moment | — |
| P-S8 | Do Not Disturb via undocumented WNF quiet-hours state | Reads Windows quiet hours | Skip — `IQuietSignal` seam; E's `QuietState` replaces the simple adapter at merge | — |
| P-S9 | Onboarding and sign-in suppression reasons | Suppresses cards during onboarding/sign-in | Skip — onboarding (H) is not built; suggestions skip the day when no provider is configured | — |

**Native-accepted: pending.** P1–P4 move to Native-accepted after the user runs these seven checks (they need a real calendar, real apps and a real call). Automated state on 25 Oct: 94 focused Core checks for the P classes above, plus `SettingsPatchTests.ProactiveFlagsDefaultOffAndPatch` (a fresh profile has every proactive flag off) and `ProactiveCoordinatorTests.AllOffShowsNothingAndCallsNothing` (all-off makes no calls); full gate 1,667 Core, 48 Input and 199 vitest (19 files) checks green, Release build 0 warnings.

1. Settings → Proactive: all four toggles are off on a fresh profile.
2. Turn on Meeting countdown, pick a calendar connector, add a meeting starting in 3 minutes: a card appears about 75 s before, counts down, and Join opens the meeting link.
3. Turn on Morning brief before noon, use the mouse: "Good morning" appears once; restarting OpenClicky does not show it again today; Plan my day sends it to chat.
4. Turn on Suggestions with a signed-in provider: up to three cards arrive one at a time; Do it sends the shown message; Don't suggest is not repeated tomorrow.
5. Turn on Connect nudges, use Slack or Gmail for 10 s: "Connect Slack?" appears; Connect opens Integrations; it does not return for 48 h.
6. Start a Teams/Zoom call or present PowerPoint: no proactive card appears, and a shown one goes away until the call ends.
7. Turn on Do Not Disturb (Focus): note whether proactive cards stay hidden; the simple adapter may miss some Focus modes, and E's detector should catch them.

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
