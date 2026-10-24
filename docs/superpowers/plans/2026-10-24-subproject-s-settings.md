# Sub-project S — Settings in the notch Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the classic SettingsForm with HeyClicky-style Settings inside the notch: a dark sidebar (Account, General | OPENCLICKY: Voice, Microphone, Dictation, Shortcuts, Cursor | WORK: Integrations, Agents, Routines | EXTRAS: Access & privacy, Memory, Notes, Clipboard shelf, Tasks, Advanced) and grouped rows of toggles, tiles, radio lists and actions. Every OpenClicky setting stays reachable.

**Architecture:** `ShellState` gains a `settings` view model built by `ShellSettingsBuilder` (Core) from AppSettings plus runtime facts (devices, models, sign-in, Whisper, privacy consent). The page edits via `settings.patch {key, value}` (allow-listed keys, validated by `SettingsPatch` in Core) and `settings.action {action, arg?}` (sign-in, refresh models, test mic, …). These run through a `SettingsService` extracted from SettingsForm, so both UIs share one implementation until the classic form is retired.

**Tech Stack:** as sub-project A.

**Spec:** [design spec](../specs/2026-10-24-heyclicky-parity-design.md) §S · ledger rows A9, B1, B4, B6, C1, D4, D5, E8, E9, F1, F4, G1 (settings surfaces only). Behaviours behind new settings land in B/C/D/E/F/G; S stores and shows them.

## Global Constraints

- Everything from sub-project A's Global Constraints.
- Secrets (OpenRouter key, connector tokens) never enter `ShellState`; the page shows "Saved" / "Not set" only, and secret entry happens in a native masked prompt (`settings.action secret.edit`).
- Defaults: talk = hold Ctrl+Alt; text mode = double-tap Ctrl; dictate = hold Ctrl+Win; hands-free dictation = double-tap Ctrl+Win; voice "windows-default"; speed 1.0; captions on; cursor colour #3380FF; hide cursor off; screen highlight on; shake to detach on; speak job announcements on; updates beside cursor on; auto-detect language on; dictionary empty (max 50 words, each ≤ 40 chars).
- Speeds: 0.5, 0.75, 1, 1.25, 1.5. Voices: Windows installed voices + OpenAI voices alloy, ash, ballad, cedar, coral, echo, marin, sage, shimmer, verse (shown as "needs your OpenAI key" until a key is saved).
- Access modes keep their warning: choosing Auto or Full from the page triggers the native warning dialog. The page never changes access silently.
- Sidebar order is fixed as in Goal. No Invite & Earn, no plans, no paywall.

## Review Focus

- A `settings.patch` with an unknown key, wrong type or out-of-range value → rejected, not persisted. Test: `SettingsPatchTests.RejectsUnknownKeyWrongTypeAndRange`.
- Choosing Full access from the page → native warning shown; declining leaves the old mode. Test: `SettingsServiceTests.FullAccessRequiresConfirmation`.
- A hotkey patch that duplicates another action's chord → rejected with a conflict message card. Test: `SettingsPatchTests.RejectsConflictingHotkeys`.
- Unplugged default microphone → list shows it as unavailable; selection falls back to System Default. Test: `ShellSettingsBuilderTests.UnavailableDeviceIsFlagged`.
- Very long dictionary/deny-list input → capped, never crashes the page. Test: `SettingsPatchTests.CapsDictionaryAndDenyList`.

---

### Task S1: Settings contract

**Files:** Modify `apps/windows/OpenClicky.Core/Shell/ShellContract.cs`, `apps/companion-workspace/src/shell/contract.ts`, `tests/fixtures/shell-state.v1.json`; Tests `ShellContractTests`, `contract.test.ts`.

**Interfaces (identical JSON both sides):**
- `ShellState.settings: SettingsView` and `ShellState.notch.section: string` (one of the sidebar ids: `account|general|voice|microphone|dictation|shortcuts|cursor|integrations|agents|routines|access|memory|notes|clipboard|tasks|advanced`).
- `SettingsView { account: { provider: "chatgpt"|"openrouter"; signedInAs: string|null; model: string; quickModel: string; models: string[]; openRouterKeySet: bool; openRouterModel: string }; voice: { voice: string; voices: { id; name; detail; available: bool }[]; speed: number; captions: bool; readReplies: bool; outputDeviceId: string|null; outputDevices: Device[] }; microphone: { deviceId: string|null; devices: Device[]; whisper: "missing"|"downloading"|"ready"; whisperProgress: number }; dictation: { autoDetect: bool; language: string; languages: { code; name; native }[]; dictionary: string[] }; shortcuts: { talk, text, dictate, handsFree: Hotkey }; cursor: { colour: hex; hidden: bool; screenHighlight: bool; shakeToDetach: bool }; integrations: { chrome: "notInstalled"|"waiting"|"connected"; connectors: { key; name; host; status: "connected"|"checking"|"error"|"needsSignIn"; toolCount: number|null }[] }; agents: { folder: string; folderIsDefault: bool; speakAnnouncements: bool; cursorUpdates: bool; reach: { id; label; state: "allowed"|"denied"|"unknown" }[] }; routines: { id; title; schedule: string; next: string|null; last: string|null; status: "scheduled"|"running"|"failed"|"paused" }[]; access: { mode: "preview"|"confirmEach"|"auto"|"full"; denyList: string; rememberHistory: bool }; advanced: { useLocalProxy: bool; proxyUrl: string; forceGdiCapture: bool } }`
- `Device { id; name; state: "active"|"unavailable" }`; `Hotkey { keys: string[]; trigger: "hold"|"doubleTap" }`.
- Page→host: `settings.section { section }`, `settings.patch { key, value }` (key from the allow-list in S2), `settings.action { action, arg? }` with action ∈ `signIn|signOut|refreshModels|manageUsage|secret.edit|testMic|downloadWhisper|playTestSound|previewVoice|clearHistory|exportMemory|openNotes|openClipboard|openTasks|chooseAgentFolder|resetAgentFolder|openPrivacySetting|routine.run|routine.pause|routine.resume|routine.delete|connector.add|connector.test|connector.remove|chrome.setup|recordHotkey|resetHotkeys|openLogs`.
- Host→page: `mic.level { level: 0..1 }`.
- [ ] Fixture covers every section; TS and C# fixture tests pass; unknown action/section rejected.
- [ ] Commit `feat(settings): settings contract`.

### Task S2: SettingsPatch, ShellSettingsBuilder (Core)

**Files:** Create `apps/windows/OpenClicky.Core/Settings/{SettingsPatch,ShellSettingsBuilder,HotkeyBinding}.cs`; Modify `AppSettings.cs` (new fields with the defaults above: `VoiceId`, `SpeechSpeed`, `Captions`, `OutputDeviceId`, `DictationAutoDetect`, `DictationLanguage`, `DictionaryWords`, `TalkHotkey`, `TextHotkey`, `DictateHotkey`, `HandsFreeHotkey`, `CursorHidden`, `ScreenHighlight`, `ShakeToDetach`, `AgentFolder`, `SpeakJobAnnouncements`, `CursorJobUpdates`); Tests `SettingsPatchTests`, `ShellSettingsBuilderTests`.

- `SettingsPatch.TryApply(AppSettings s, string key, JsonElement value, out string? error) -> PatchResult { Applied | Rejected | RequiresConfirmation }` with a fixed key table (type, range, caps): `voice.speed` ∈ the five speeds, `cursor.colour` ∈ the four accents, `dictation.dictionary` ≤ 50 words × 40 chars (trimmed, deduped case-insensitively), `access.denyList` ≤ 2,000 chars, `shortcuts.*` validated `Hotkey` with conflict check, `access.mode` auto/full → `RequiresConfirmation`.
- `ShellSettingsBuilder.Build(AppSettings, SettingsRuntime) -> SettingsView`; `SettingsRuntime` carries devices, models, sign-in, whisper state, connectors, routines, consent states, Chrome state.
- Existing `PushToTalk` (`HotkeyChord`) keeps working: until sub-project B, the talk hotkey shown is the configured chord; `TalkHotkey` default applies when B switches the monitor.
- [ ] Tests from Review Focus 1, 3, 4, 5 plus a defaults test.
- [ ] Commit `feat(settings): patch validation and settings view builder`.

### Task S3: Native wiring

**Files:** Create `apps/windows/OpenClicky.App/Settings/{SettingsService,SettingsRuntimeProbe,PrivacyConsent}.cs`; Modify `SettingsForm.cs` (use SettingsService for its save/sign-in/model/mic/whisper/clear logic, behaviour unchanged), `TrayApplicationContext.Shell.cs` (route `settings.*` intents; tray "Settings" opens the notch Settings tab; classic SettingsForm stays reachable from the Classic window); Tests `SettingsServiceTests` (logic in Core behind interfaces).

- `SettingsRuntimeProbe` enumerates capture/render devices (NAudio MMDeviceEnumerator, state active/unplugged), Windows voices (System.Speech InstalledVoices), Whisper model state, ChatGPT models (existing refresh path), connectors (ConnectorRegistry), routines (WorkStore), Chrome bridge state.
- `PrivacyConsent` reads `HKCU\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\{documentsLibrary,picturesLibrary,contacts,appointments,microphone,webcam,location}\Value` ("Allow"/"Deny") → reach rows; `openPrivacySetting` opens `ms-settings:privacy-<page>`.
- `secret.edit` opens a small native masked prompt for the OpenRouter key; stored only in Credential Manager.
- `access.mode` auto/full → existing warning MessageBox; on accept apply, else re-push old state.
- [ ] Tests for FullAccessRequiresConfirmation (fake confirm) and patch → persist → push.
- [ ] Commit `feat(settings): native settings service and routing`.

### Task S4: Settings layout, Account, General, Access & privacy, Advanced (web)

**Files:** Create `src/shell/settings/{SettingsPanel,Sidebar,AccountPage,GeneralPage,AccessPage,AdvancedPage}.tsx`, `settings.css`; Modify `notch/Peek.tsx` (Settings tab renders SettingsPanel); Tests `settings/settings.test.tsx`.

- Panel 920×720: sidebar 240 px with group micro-headers (small caps), active item highlighted; content scrolls; page header = title + one-line subtitle (our own copy); groups = `Group` cards with `Row`/`ToggleRow`/`ActionRow`.
- Account: provider segmented (ChatGPT plan / OpenRouter); ChatGPT: "Signed in as …" + Sign out, or "Continue with ChatGPT" primary pill; model and quick-reply model pickers (RadioList in a Group), Refresh models, Manage usage; OpenRouter: key "Saved"/"Not set" + Change (secret.edit), model text; APP group: Quit OpenClicky (danger).
- General: BEHAVIOUR toggles (Show in taskbar, Show in screen recordings, Launch at login, Pause OpenClicky) → `general.set`; UPDATES row (version, Check for updates / Downloading n% / Install) + What's new link; HELP rows (Report a bug, Request a feature).
- Access & privacy: access mode RadioList with one-line consequences; protected window titles (textarea, saved on blur); Remember conversations toggle; Clear conversation history (danger, in-page confirm).
- Advanced: local proxy toggle + URL; compatibility capture toggle; Open logs.
- [ ] Tests: section click posts `settings.section`; toggles post `general.set`/`settings.patch` with the right key; access mode posts patch (host decides confirmation); clear history asks in-page confirm first.
- [ ] Commit `feat(settings): layout, account, general, access, advanced`.

### Task S5: Voice, Microphone, Dictation, Shortcuts, Cursor pages (web)

**Files:** Create `src/shell/settings/{VoicePage,MicrophonePage,DictationPage,ShortcutsPage,CursorPage}.tsx`; Tests alongside.

- Voice: SPEECH SPEED chips; VOICE tile grid (5 per row; unavailable tiles dimmed with "Needs OpenAI key"); tap = `previewVoice` + patch; CAPTIONS toggle; READ REPLIES toggle; OUTPUT DEVICE RadioList + Play a test sound.
- Microphone: INPUT DEVICE RadioList (unavailable greyed); TEST: Test microphone + live Meter driven by `mic.level`; LOCAL VOICE: Whisper status + Download (progress).
- Dictation: Auto-detect toggle; language search + tile grid (disabled while auto); DICTIONARY: add word field + chips with remove (cap 50).
- Shortcuts: four rows with KeyCaps + Change (`recordHotkey` arg = action; host records and pushes; page shows "Press your keys…" while recording) + Reset to defaults.
- Cursor: COLOUR four glowing-triangle tiles (accent); VISIBILITY toggles Hide floating cursor, Screen highlight, Shake to detach.
- [ ] Tests: speed chip posts `voice.speed`; dictionary cap 50 enforced in UI; Change posts `recordHotkey` with the action; colour tile posts `cursor.colour`.
- [ ] Commit `feat(settings): voice, microphone, dictation, shortcuts, cursor`.

### Task S6: Integrations, Agents, Routines, Memory, Notes, Clipboard, Tasks pages

**Files:** Create `src/shell/settings/{IntegrationsPage,AgentsPage,RoutinesPage,ExtrasPages}.tsx`; Modify host builder for routines/connectors/memory counts; Tests alongside.

- Integrations: BROWSER card (Chrome status badge, Set up / Disconnect); search; CONNECTED list (name, host, status chip, tool count, Test, Remove); "Add custom connector" opens an inline form (Name, Server URL, Authentication segmented Sign in with browser | API key | None — browser sign-in shows "Arrives with the connectors update" until sub-project F), Cancel / Add connector (`connector.add`, secrets via native prompt).
- Agents: WORKSPACE agent folder row (path, Change, Reset); ANNOUNCEMENTS toggles; WHAT AGENTS CAN REACH list with Allowed/Denied badges, tap opens the Windows privacy page.
- Routines: rows (title, schedule words, next/last, status) with Run now, Pause/Resume, Delete (in-page confirm); empty state "Hold {talk keys} and tell OpenClicky what to do, and when." plus an example in our own words.
- Extras: Memory (count + Open + Export), Notes (Open), Clipboard shelf (Open), Tasks (Open timeline) — each a short page with a description and an Open button that opens the existing native panel.
- [ ] Tests: add-connector form requires an https URL, routine actions post correct ids, reach row posts openPrivacySetting.
- [ ] Commit `feat(settings): integrations, agents, routines, extras`.

### Task S7: Native acceptance

- [ ] Run the built app, open Settings from the gear and from the tray, screenshot each section, change a toggle and confirm `settings.json` changed, switch access to Auto and confirm the warning dialog appears (then cancel), confirm the classic SettingsForm still opens from the Classic window. Update ledger rows.
