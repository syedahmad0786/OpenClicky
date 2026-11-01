# HeyClicky parity for OpenClicky — design

Date: 24 October 2026 · Branch: `heyclicky-parity` · Ledger: [feature-ledger.md](../../heyclicky-parity/feature-ledger.md)

## Intent (agreed with the user)

- Make OpenClicky work and look like HeyClicky for Windows 1.1.1, feature for feature, keeping every OpenClicky extra.
- Clean-room: match layout, navigation, flows and feel; never copy HeyClicky code, prompt text, art, sounds, fonts or wording; never call HeyClicky's backend, Supabase or Stripe. OpenClicky keeps its own name and logo.
- Not reproduced: HeyClicky accounts, plans/paywall, Invite & Earn, analytics, crash reporting.
- Safety rules stay: Confirm-each default; Auto/Full need visible warning; a running task's access never silently widens; no Full access on the user's behalf; secrets stay in Credential Manager.
- "Done" = every ledger row built and checked in the running app (or marked Skip with a reason), plus side-by-side screenshots for UI rows.
- Approved approach: **one React design system rendered in transparent WebView2 windows**, native code for the cursor/drawing overlay, hotkeys, audio, capture and agents. Build all sub-projects in order without stopping; anything needing the user goes to their phone.

## Architecture

```
TrayApplicationContext (existing, native)
 ├─ ShellController ── ShellSurfaceWindow (per active monitor, transparent, click-through outside page regions)
 │                      └─ WebView2 → workspace runtime → /shell.html (React "shell" app)
 │                           Notch · Peek tabs (Home, Chat, Settings, Document) · Notch cards · Job deck · Captions · Onboarding
 ├─ BuddyOverlayController (native; restyled companion cursor, drawing, highlight)
 ├─ Hotkeys / audio / capture / Whisper / TTS (native)
 └─ Stores: settings, conversation, memory, notes, clipboard, WorkStore (companions, jobs, routines), ConnectorRegistry
```

- **One surface, many components.** A single full-monitor `ShellSurfaceWindow` hosts the whole shell page. The page reports the rectangles it draws (`surface.regions`); everything else passes clicks to the apps below. The surface is non-activating, and activates only while a page input needs the keyboard. It moves to the monitor under the cursor while collapsed.
- **Same runtime.** The existing Node workspace runtime serves a second Vite page, `shell.html`, under the same loopback origin, token, CSP and message-security rules. The old workspace page stays for the visuals workbench.
- **Typed contract.** Host → page: `shell.state` (a complete versioned view model), plus streaming messages (`chat.delta`, `caption.word`, `activity`). Page → host: intents (`talk.toggle`, `text.submit`, `settings.patch`, `job.open`, `job.stop`, `routine.run`, `connector.add`, …). The schema is defined once in zod (TypeScript) and once as C# records. A shared JSON fixture is tested on both sides.
- **View model builder.** `ShellStateBuilder` (Core, unit-tested) turns settings, stores and runtime status into `ShellState`. `ShellController` (App) owns the surface, pushes state on change (coalesced at 60 ms) and routes intents to existing TrayApplicationContext operations through a narrow `IShellHost` interface. New logic lives outside the 1,500-line tray context.
- **Legacy windows** (HomeForm, SettingsForm, Clipboard shelf, Memory, Notes, Tasks, Work) stay reachable from the tray as "Classic window" until their React replacements land. Then they are removed (moved to `Work\_Graveyard` only if unneeded at runtime).

## Design system (sub-project A)

- **Tokens:** CSS custom properties in `src/shell/theme.css`; dark and light sets chosen from Windows `AppsUseLightTheme` (passed in state).
  - Surfaces: near-black panel, raised card one step lighter, hairline borders.
  - Text: primary, secondary, tertiary.
  - Status: success green, danger red, warning amber.
  - Accent: the cursor colour (Blue `#3380FF` default, Red `#FF4C4C`, Yellow `#D4A017`, Green `#26C281`).
- **Type:** Segoe UI Variable (system); handwriting accents on paper cards use the system Segoe Print. No fonts are bundled.
- **Components:**
  - Buttons and labels: `PillButton` (primary gradient in the accent / secondary white / outline / danger-text), `IconButton`, `Chip`, `KeyCaps`.
  - Settings building blocks: `Toggle`, `Segmented`, `SectionHeader` (small caps), `Group` (rounded card with dividers), `Row`/`ToggleRow`/`ActionRow`, `RadioList`, `Tile` (voice/colour/language), `SearchField`, `Meter`.
  - Paper: `PaperCard` (paper stocks, tilt, tape/handle detail).
- **Motion:** spring-like CSS transitions (open 430 ms, close 300 ms, content fade 180 ms), and a 120 ms fade when Windows reduced motion is on.
- **Notch geometry:**
  - Collapsed pill 60×8 px at the top centre.
  - Expanded peek 532 px wide, up to 280 px of content (Settings grows to a 920×720 panel).
  - Top corners 10 px, sheet corners 26 px.
  - Hover forgiveness 28 px with a 150 ms grace.

## Sub-project designs (summary; each gets its own plan before it starts)

- **A — Shell:** surface window; shell page; notch with Home/Chat/Settings/Document tabs; notch card framework with all card types; job deck/carousel; tray menu (Open Settings, Pause/Resume, Report a bug → GitHub issue form, Request a feature → GitHub discussion, What's new → releases, Check for updates, Quit, Classic window).
  - General behaviour: show in taskbar via a hidden owner window, show in screen recordings via `WDA_EXCLUDEFROMCAPTURE`, launch at login via HKCU Run `--background`, pause.
  - Updates: GitHub Releases check every hour, "Download & restart" for the portable ZIP, "What's new".
- **S — Settings** in the peek, sidebar order:
  - Account (ChatGPT sign-in/model, OpenRouter, sign-out, Quit), General, Voice, Microphone, Dictation, Shortcuts, Cursor, Integrations, Agents, Routines.
  - OpenClicky extras group: Access & privacy (access modes, deny list, history), Memory, Notes, Clipboard shelf, Tasks, Advanced.
  - Invite & Earn is left out.
- **B — Talk:**
  - Shortcuts: hold Ctrl+Alt to talk; double-tap Ctrl for text mode; hold Ctrl+Win to dictate; double-tap Ctrl+Win for hands-free. Modifier-only chords use the tap/double-tap/hold timings 0.4 s / 0.5 s and a 7 s recorder timeout, with conflict detection.
  - Text-mode card. Voices: Windows voices offline, plus OpenAI voices (alloy, ash, ballad, cedar, coral, echo, marin, sage, shimmer, verse) with the user's own OpenAI key, previewed by generating a sample.
  - Speed 0.5–1.5×; captions word by word; output-device choice and test sound; muted-speaker and mic-trouble cards.
  - Every display is captured per turn, with our own overlays excluded.
- **C — Companion:**
  - Cursor: a rounded glowing triangle in the accent colour on a spring, with listening bars, thinking dots, a message bubble and a label bubble. It can dock into the notch, hide, or shake-to-detach.
  - Drawing: the full primitive set and screen helpers, plus Direct3D figures.
  - Gestures: draw on screen while holding talk (circle/line/scribble classification, with UIA text under each stroke).
  - Walkthroughs snap to UIA controls and advance on the user's real click.
  - Saved drawings with replay and video export.
  - Drive: a consent card plus an Esc pill, layered on OpenClicky's access modes. ConfirmEach still asks per action.
- **D — Dictation:** hold or hands-free, insertion at the caret using the existing verified insertion, UIA context sent with the cleanup request, a 50-word dictionary with learned corrections, and about 50 languages (Whisper multilingual model, offered as a download) plus auto-detect.
- **E — Work:**
  - Jobs: up to 80, colour tags, rail phases (Queued/Working/NeedsYou/Done/Failed/Stopped). Folders hold `output/`, `tmp/`, `note.md` and a `WORK.md` index.
  - Results: a structured final reply parser; live plan and progress; spoken start/finish announcements; an update bubble beside the cursor; quiet during calls, screen share and Do Not Disturb.
  - Control: steer and interrupt, plus up to 5 concurrent jobs on the OpenWork engine.
  - User steps: a "your turn" watcher. How-tos: learned (up to 60; newest 10 plus 5 most related).
  - Routines:
    - Created by voice through a `routines` tool; the Settings list offers Run now, Pause and Delete.
    - Scheduler: 30 s tick, 2 h stale limit, runs 3 min after wake, waits for network, keeps awake on AC.
    - Failures: retry after 300 s; pause after 3 failures in a row.
  - Agent settings: agent folder; what agents can reach, from the Windows privacy consent store.
- **F — Connectors:**
  - Integrations page with search, Connected/Available, and status chips.
  - Multiple labelled accounts per app.
  - Custom MCP: remote URL or local command; browser sign-in (discovery, dynamic client registration, PKCE, `resource`, loopback callback, refresh), API key, or none; advanced headers/env stored encrypted; verify with initialize + tools/list.
  - Built-in list of public MCP servers, the user's own Composio URL, and an Obsidian vault.
- **G — Browser:**
  - Extension moves to the Chrome debugging protocol with the 24 operations, page diffs, stable element refs and refusal to type into password fields.
  - One tab group per Clicky in a shared work window; native host relay; browser card with detection, install help and profiles.
- **H — Onboarding:** intro (welcome, colour pick), permission cards (microphone, screen), and a hands-on tour (mic check, voice hello, draw demo, circle-to-ask, drive demo, text mode, dictation, finale). All copy and art are our own.
- **P — Proactive:** morning brief, suggestion cards, integration nudges (10 s dwell, at most 3 per 24 h, 48 h snooze), and a meeting countdown from the user's calendar connector. Off by default, like HeyClicky 1.1.1.

## Error handling

- The shell page always renders from the last good `shell.state`. If the workspace runtime is down, `ShellController` falls back to the classic Home window and shows a tray balloon.
- Rejected intents (validation, policy) come back as `toast` cards.
- Surface failures (WebView2 missing, composition unavailable) disable the shell and keep the classic UI.

## Testing

- **Unit:** C# xUnit for builders, parsers, schedulers, hotkey state machines and policies; vitest for zod contracts and components (render + interaction).
- **Native:** launch the built app, drive the real shell via UI Automation and screenshots, and inspect the real destinations (files, inserted text, Notepad contents).
- **Side by side:** the same screen in HeyClicky (view-only) and OpenClicky, screenshots stored under `docs/heyclicky-parity/compare/`.
- **Build gate:** `dotnet build OpenClicky.sln -c Release` 0 warnings, all tests green, `pnpm --dir apps/companion-workspace test` green.
