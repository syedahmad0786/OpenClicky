# Sub-project A — Shell, design system and notch Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace OpenClicky's Home window with a HeyClicky-style notch shell: one transparent, click-through WebView2 surface that renders the notch (Home/Chat/Settings/Document tabs), notch cards and the job deck from a typed view model.

**Architecture:** React page `shell.html` (served by the existing loopback workspace runtime) renders everything from `ShellState`. The page reports drawn rectangles, and `ShellSurfaceWindow` passes clicks elsewhere to the apps below. `ShellStateBuilder` (Core) builds the state; `ShellController` (App) owns the surface and maps intents onto existing operations through `IShellHost`.

**Tech Stack:** .NET 8 WinForms + WebView2 1.0.4258.31 (visual hosting), React 19 + Vite 7 + zod 3, xUnit, vitest.

**Spec:** [docs/superpowers/specs/2026-10-24-heyclicky-parity-design.md](../specs/2026-10-24-heyclicky-parity-design.md) · ledger rows A1–A10.

## Global Constraints

- Clean-room: no HeyClicky code, prompt text, art, sounds, fonts or wording. Our own copy for every label.
- Loopback-only origin, token bootstrap, bounded JSON messages (max 128,000 chars), message-kind allow-lists on both sides, no devtools/context menus/host objects/new windows.
- `dotnet build OpenClicky.sln -c Release` with 0 warnings; `dotnet test OpenClicky.sln -c Release --no-build` green; `pnpm --dir apps/companion-workspace test` green.
- Accent default `#3380FF`; alternatives `#FF4C4C`, `#D4A017`, `#26C281`. Theme follows Windows `AppsUseLightTheme`.
- Notch: collapsed 60×8 px at top centre; peek width 532 px; peek content up to 280 px; settings panel 920×720; top corners 10 px; sheet corners 26 px; hover forgiveness 28 px, grace 150 ms; open 430 ms, close 300 ms, fade 180 ms, reduced motion 120 ms fade.
- Caps: jobs ≤ 80, chat rows ≤ 200, notch cards ≤ 6, job bullets ≤ 4, job files ≤ 6, card actions ≤ 3, document content ≤ 200,000 chars.
- Safety: `file.open`/`file.reveal` intents are honoured only for paths the host itself put in the current state.

## Review Focus

- A page message arriving before bootstrap or with an unknown kind → ignored, never crashes the host. Test: `ShellIntentParserTests.UnknownOrOversizedKindIsRejected`.
- `file.open` with a path not present in the last pushed state (path traversal, UNC) → refused. Test: `ShellControllerTests.FileOpenOutsideStateIsRefused`.
- Monitor unplugged / DPI change while expanded → the surface re-fits to the current monitor and regions stay correct. Test: `ShellRegionMathTests.ClampsToMonitorAfterDpiChange`.
- Shell runtime fails to start → the classic Home window opens and the tray still works. Test: `ShellControllerTests.FallsBackToClassicWhenRuntimeFails`.
- Long titles or many jobs → layout truncates with ellipsis and "+N more" without overflow. Test: `JobDeck.test.tsx > collapses beyond visible count into +N more`.

---

### Task A1: ShellSurfaceWindow (from spike)

**Files:**
- Create: `apps/windows/OpenClicky.App/Surfaces/ShellSurfaceWindow.cs` (+ helpers in `Surfaces/`)
- Create: `apps/windows/OpenClicky.Core/Shell/ShellRegionMath.cs`
- Test: `tests/OpenClicky.Core.Tests/ShellRegionMathTests.cs`

**Interfaces:**
- Produces: `ShellSurfaceWindow(Uri origin, string sessionToken, IReadOnlySet<string> allowedKinds)`; `Task InitializeAsync(string userDataFolder)`; `void Post(object message)`; `event EventHandler<SurfaceMessageEventArgs> Message` (`Kind`, `Json`); `void MoveToMonitor(Screen screen)`; `void SetKeyboardFocus(bool wanted)`; `bool ExcludeFromCapture { get; set; }`.
- Produces: `ShellRegionMath.ToDevice(IReadOnlyList<RegionRect> cssRects, double scale, Rectangle monitorBounds) -> IReadOnlyList<Rectangle>` (drops empty, clamps to monitor, caps 64 rects); `record RegionRect(double X, double Y, double W, double H, double R)`.

- [ ] Test `ToDeviceScalesClampsAndCaps`: scale 1.5, rect (10,10,100,20) → (15,15,150,30); a rect past the right edge is clamped; 70 rects → 64.
- [ ] Test `ClampsToMonitorAfterDpiChange`: same CSS rects at scale 1.0 then 2.0 stay inside the monitor bounds.
- [ ] Implement from the spike's working technique; keep VisualWorkspacePanel's navigation and message rules.
- [ ] Build + tests green; commit `feat(shell): transparent click-through WebView2 surface`.

### Task A2: Shell contract (TypeScript + C#)

**Files:**
- Create: `apps/companion-workspace/src/shell/contract.ts`, `apps/windows/OpenClicky.Core/Shell/ShellContract.cs`, `tests/fixtures/shell-state.v1.json`
- Test: `apps/companion-workspace/src/shell/contract.test.ts`, `tests/OpenClicky.Core.Tests/ShellContractTests.cs`

**Interfaces (both sides, identical JSON, camelCase):**
- `ShellState { version: 1; theme: "dark"|"light"; accent: hex; reducedMotion: bool; user: { displayName }; hotkeys: { talk, text, dictate, handsFree: string[] }; status: { phase: "idle"|"listening"|"thinking"|"speaking"|"working"|"paused"; jobsWorking; jobsNeedYou }; notch: { tab: "home"|"chat"|"settings"|"document"; open: bool }; featured: JobCard|null; jobs: JobCard[]; chat: { rows: ChatRow[] }; cards: NotchCard[]; apps: { connected: { id, name }[] }; general: { showInTaskbar, showInRecordings, launchAtLogin, paused: bool }; update: { current: string; available: string|null; state: "idle"|"checking"|"available"|"downloading"|"ready"|"upToDate"|"error"; progress: number }; document: DocumentView|null }`
- `JobCard { id; title; state: "queued"|"working"|"needsYou"|"done"|"failed"|"stopped"|"new"; colour: "red"|"blue"|"yellow"|"green"; summary?; stat?: { value; label }; bullets: string[]; files: { name; path; kind: "doc"|"pdf"|"sheet"|"image"|"audio"|"video"|"link"|"folder"|"other" }[]; liveLine?; plan: { text; done: bool }[]; updatedAt: ISO string }`
- `ChatRow { id; role: "user"|"assistant"|"event"; text; at: ISO; jobId? }`
- `NotchCard { id; kind: "activity"|"textResponse"|"jobUpdate"|"needsYou"|"mutedSpeaker"|"micTrouble"|"clipboard"|"dictionaryLearned"|"documentAccess"|"appUpdated"|"tip"|"toast"; title; body?; actions: { id; label; style: "primary"|"secondary"|"danger" }[]; autoDismissMs? }`
- `DocumentView { path; name; kind: "markdown"|"text"|"csv"|"pdf"; content; truncated: bool }`
- Host→page kinds: `session.bootstrap`, `shell.state`, `chat.delta { rowId, text }`, `caption.word { text }`.
- Page→host kinds: `bootstrap.request`, `ready`, `surface.regions { rects }`, `surface.focus { wanted }`, `notch.tab { tab }`, `notch.open { open }`, `talk.toggle`, `text.submit { text }`, `general.set { key, value }`, `job.open|job.stop|job.dismiss { id }`, `file.open|file.reveal { path }`, `card.action { cardId, actionId }`, `card.dismiss { cardId }`, `update.check`, `update.install`, `link.open { target: "whatsNew"|"bug"|"feature" }`, `app.quit`.
- C#: `ShellContract.Serialize(ShellState) -> string`, `ShellIntentParser.TryParse(string json, out ShellIntent intent) -> bool` (kind allow-list, max 128,000 chars, MaxDepth 20).

- [ ] Fixture contains one of every card kind and job state; the vitest test `parses the v1 fixture` and the xUnit `FixtureRoundTrips` both pass on it.
- [ ] Tests `UnknownOrOversizedKindIsRejected` (C#) and `rejects unknown kinds` (TS).
- [ ] Commit `feat(shell): typed shell contract`.

### Task A3: Shell page entry

**Files:** Modify `apps/companion-workspace/vite.config.ts` (multi-page `index.html` + `shell.html`); Create `apps/companion-workspace/shell.html`, `src/shell/main.tsx`, `src/shell/ShellApp.tsx`, `src/shell/bridge.ts` (reuses the host-bridge pattern with the shell contract). `CompanionWorkspaceRuntime` health check stays on `index.html`.

- [ ] `pnpm build` emits `dist/shell.html`; vitest `ShellApp renders nothing until shell.state arrives, then the collapsed notch`.
- [ ] Commit `feat(shell): shell page entry`.

### Task A4: Design system

**Files:** Create `src/shell/theme.css`, `src/shell/ui/{PillButton,IconButton,Chip,KeyCaps,Toggle,Segmented,SectionHeader,Group,Row,RadioList,Tile,SearchField,Meter,PaperCard}.tsx`, `src/shell/ui/index.ts`; Test `src/shell/ui/ui.test.tsx`.

- Tokens on `:root[data-theme=dark|light]` and `--accent`; components are presentational, keyboard accessible (buttons are `<button>`, toggles `role="switch"` with `aria-checked`), no external icons (inline SVG paths of our own).
- [ ] Tests: Toggle flips `aria-checked` and calls `onChange`; Segmented arrow keys move selection; KeyCaps renders one cap per key; PillButton primary uses `--accent`.
- [ ] Commit `feat(shell): design system`.

### Task A5: Notch, peek, Home and Chat tabs, regions

**Files:** Create `src/shell/notch/{Notch,Peek,HomeTab,ChatTab,TabSwitcher,useRegions}.tsx(.ts)`; Test `src/shell/notch/notch.test.tsx`.

- `useRegions()` measures every element with `data-region` (rect + border radius) on layout/resize/animation end and posts `surface.regions` (debounced 16 ms).
- Notch opens on hover of the collapsed pill or `notch.open`; closes after leaving the panel plus 28 px forgiveness for 150 ms; tabs via `TabSwitcher` (Home, Chat) + gear (Settings) on the right.
- Home: greeting by local hour (5–12 "Good morning", 12–17 "Good afternoon", 17–22 "Good evening", else "Hi there") + `user.displayName`; our own hint "Hold {talk keys} and ask for anything"; featured job card; jobs status line ("{n} working · {m} need you"); connected-apps pill → Settings tab.
- Chat: rows, streaming `chat.delta`, composer (`text.submit` on Enter, Shift+Enter newline, focus requests `surface.focus`), talk button (`talk.toggle`) showing the talk keys, empty state.
- [ ] Tests: hover opens/leave closes after grace; greeting per hour; Enter submits and clears; regions posted contain the panel rect.
- [ ] Commit `feat(shell): notch with Home and Chat`.

### Task A6: Notch cards, job deck, document tab

**Files:** Create `src/shell/cards/NotchCards.tsx`, `src/shell/deck/{JobDeck,JobCardView}.tsx`, `src/shell/notch/DocumentTab.tsx`; Tests alongside.

- Cards stack under the notch (max 3 visible), auto-dismiss with pause-on-hover, actions post `card.action`.
- Job deck: paper cards alternately tilted ±3–5° left and right of the notch, newest nearest; beyond 3 per side a "+N more" pill expands; clicking a card opens it as featured in Home (`job.open`).
- Document tab: markdown (own minimal renderer: headings, lists, code, links as text), text, CSV as a table with sum/average/count of the selected numeric column, PDF shows "Open in its app" only.
- [ ] Tests: auto-dismiss pauses on hover; "collapses beyond visible count into +N more"; CSV stats computed.
- [ ] Commit `feat(shell): cards, job deck, document viewer`.

### Task A7: Native shell controller and tray

**Files:** Create `apps/windows/OpenClicky.Core/Shell/ShellStateBuilder.cs`, `apps/windows/OpenClicky.App/Shell/{ShellController,IShellHost,GeneralBehaviour}.cs`; Modify `TrayApplicationContext.cs` (create controller after workspace runtime starts; tray menu items; Home → notch; Classic window entry), `AppSettings.cs` (+`AccentColor`, `ShowInTaskbar`, `ShowInScreenRecordings`, `LaunchAtLogin`, `Paused`, `DisplayName`); Tests `ShellStateBuilderTests`, `ShellControllerTests`.

- `ShellStateBuilder.Build(ShellInputs inputs) -> ShellState` where `ShellInputs` carries settings, jobs (`WorkStore`), chat history, connectors, status, update state, cards.
- `IShellHost` methods: `ToggleTalk()`, `SubmitText(string)`, `OpenJob(string)`, `StopJob(string)`, `DismissJob(string)`, `OpenSettingsClassic()`, `Quit()`, `CheckForUpdates()`, `InstallUpdate()`, `SetGeneral(string key, bool value)`.
- `GeneralBehaviour`: `SetLaunchAtLogin(bool)` writes/deletes `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\OpenClicky = "<exe>" --background`; `ApplyCaptureExclusion(nint hwnd, bool exclude)`; taskbar presence via a hidden `ShowInTaskbar` owner form.
- Tray menu order: Open OpenClicky, Settings, Pause/Resume, Classic window, What's new, Report a bug, Request a feature, Check for updates, Quit.
- [ ] Tests: builder caps lists and maps job states; `FileOpenOutsideStateIsRefused`; `FallsBackToClassicWhenRuntimeFails` (fake runtime factory throws → classic shown).
- [ ] Commit `feat(shell): native controller, tray and general behaviour`.

### Task A8: Updates

**Files:** Create `apps/windows/OpenClicky.Core/Updates/{ReleaseInfo,UpdateChecker}.cs`; Test `UpdateCheckerTests`.

- `UpdateChecker(HttpClient http, string currentVersion)`; `Task<ReleaseInfo?> CheckAsync(CancellationToken)` reads `https://api.github.com/repos/syedahmad0786/OpenClicky/releases/latest` (User-Agent `OpenClicky`), compares semver (ignores `v` prefix, pre-release lower than release), returns the portable ZIP asset URL. Hourly from the controller. "Install" downloads to `%LOCALAPPDATA%\OpenClicky\updates\`, verifies size and that the ZIP contains `OpenClicky.exe`, then opens the folder and the release page (automatic swap is a later step).
- [ ] Tests with a fake handler: newer → available; same → upToDate; malformed JSON → null; pre-release ignored.
- [ ] Commit `feat(updates): GitHub release check`.

### Task A9: Native acceptance

- [ ] Build and run `bin\x64\Release\...\OpenClicky.App.exe`; screenshot collapsed notch, Home, Chat, cards, deck; click-through check (click a window under the surface); compare with HeyClicky screenshots into `docs/heyclicky-parity/compare/A-*.png`; update ledger rows A1–A10.
