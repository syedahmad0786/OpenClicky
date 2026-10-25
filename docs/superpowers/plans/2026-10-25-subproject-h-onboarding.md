# Sub-project H — Onboarding: intro, permission cards, hands-on tour Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** On first run, and again from Settings → General, OpenClicky shows a skippable tour card under the notch:
- an intro (welcome, then pick the cursor colour);
- two permission cards (microphone, screen) that explain what leaves the PC and open the matching Windows page, without changing anything;
- a hands-on tour that drives the existing features: mic check, voice hello, draw demo, circle-to-ask, drive demo, text mode, dictation and a finale that opens Home.

**Architecture:**
- **Core (pure, unit-tested):**
  - `OpenClicky.Core/Shell/OnboardingContract.cs`: the step enum, the view record and the action allow-list.
  - `OpenClicky.Core/Onboarding/OnboardingFlow.cs`: the step machine.
  - `OpenClicky.Core/Onboarding/OnboardingDrawDemo.cs`: the canned, offline draw-demo reply and its `ITalkClient`.
- **Shell controller:** carries the view on the state (`ShellState.Onboarding`, an init property, so no constructor changes) and routes one new page intent, `onboarding`, to `IShellHost.OnboardingAction` (a default interface method).
- **App:** a new partial, `OpenClicky.App/Onboarding/TrayApplicationContext.Onboarding.cs`. It owns the flow, starts it on first run, runs the demos through the existing entry points and listens to the existing hotkey and talk events.
- **Shell page:** a new `src/shell/onboarding/` folder (panel + steps + css), rendered by `Notch` between the notch and its cards, plus one "Welcome tour" row on the General page.

**Tech Stack:** .NET 8 WinForms tray app, React + zod shell, vitest, xUnit. **No new NuGet or npm packages.**

**Spec:** [design spec](../specs/2026-10-24-heyclicky-parity-design.md) §H · ledger row A11 ([feature-ledger.md](../../heyclicky-parity/feature-ledger.md)) · behaviour evidence in [ui-inventory §4](../../research/heyclicky-1.1.1/ui-inventory.md) ("Onboarding / first run").

## Research gaps

- The inventory gives the step order, the step purposes and the chrome (progress dots, Back/Continue, skip). That is everything a clean-room rebuild may use.
- Its open unknowns are layout, copy and the EmailDraft/Dictation page copy. We author those ourselves, so no new reverse-engineering pass is needed.

## Current code facts (from the survey, 25 October 2026)

- **First run:**
  - `AppSettings.FirstRunCompleted` (bool) exists.
  - `TrayApplicationContext.MaybeShowFirstRunTip()` (:681) only sets it to `true` and saves, at every first start, before the shell is up. It is called once (:318).
  - Nothing else reads the flag. Existing installs therefore already have `true`, so the tour will not pop up for upgraders.
- **Shell start:** `StartShellAsync` (`Shell/TrayApplicationContext.Shell.cs`) awaits `_shell.StartAsync()`; inside `if (started)` it applies taskbar, capture exclusion and the dock. The tour starts there.
- **Page intents:**
  - `ShellIntentParser.IsValid` (ShellContract.cs) is the allow-list switch.
  - `ShellSurfaceAdapter.PageKinds` (App/Shell) lists the kinds the window forwards. Every new kind must be added there too (`drawing.action` is missing from it today; that is out of scope here and reported).
  - `ShellController.OnMessage` routes kinds to `IShellHost`.
- **State:**
  - `ShellController.PushNow` builds `ShellStateBuilder.Build(inputs)` and posts it.
  - `SetUpdate(ShellUpdate)` is the pattern for controller-held state.
  - `ShellState` is a positional record, mirrored by the zod `shellStateSchema` (`.strict()`).
- **Settings already in state that the tour reads** (no new fields needed):
  - `hotkeys.talk/text/dictate/handsFree` (key names), and `settings.shortcuts.*.trigger`;
  - `settings.cursor.colour` and `settings.cursor.screenHighlight`;
  - `settings.account.signedInAs` and `settings.account.openRouterKeySet`;
  - `settings.microphone.devices/deviceId/whisper`;
  - `settings.agents.reach` (`microphone` item: allowed/denied/unknown, refreshed by `SettingsProbe.Refresh()`);
  - `status.phase`.
- **Existing page→host messages the tour reuses:**
  - `settings.patch` for `cursor.colour` and `cursor.screenHighlight`;
  - `settings.action testMic`, which streams `mic.level` to the page;
  - `settings.action openPrivacySetting` with arg `microphone`, which opens `ms-settings:privacy-microphone`;
  - `notch.tab` / `settings.section` for opening a settings page.
  - `useMicLevel()` (MicrophonePage.tsx) and `GlowingCursor` (CursorPage.tsx) are file-local today; this plan exports them.
- **Talk:**
  - `_talk.SubmitTextAsync(text, remember, displayAs)`.
  - The talk-client factory (TrayApplicationContext.cs :113) is `() => _demoTalk ? new OfflineTalkClient() : ProviderFactory.CreateTalkClient(...)`.
  - `TryPointingDemoAsync` shows the offline-demo pattern.
  - `_talk.TurnCommitted`, `_talk.ClearDrawing()` and `_talk.CancelCurrentTurn()` exist.
  - Replies are parsed by `DrawCommandParser.Parse(reply)` → `(Text, Ops)`. Tags look like `[DRAW:circle x=.. y=.. r=.. label="…"]`, `[DRAW:arrow pts=x1,y1,x2,y2]`, `[DRAW:sticky x y text="…" stock=butter]` and `screen=N`. Points outside the screenshot are dropped.
- **Drive:**
  - `TrySandboxDemoAsync()` (TrayApplicationContext.cs :660):
    - opens `OpenClicky.Sandbox.SandboxForm`;
    - sets `_sandboxDemo`, so `StartAgentTask` uses `SandboxPlanner` (offline: click the green button, focus the field, type "hello");
    - forces `AgentAccessMode.ConfirmEach`;
    - makes the sandbox the only trusted target in `WinFormsAgentConfirm`;
    - ends with `ShowClassic()`.
  - C12 adds the consent card before the first input and the Esc pill.
  - `SandboxForm.HasVerifiedResult` is true when the button was clicked and the field holds "hello".
  - `HomeForm` takes `TrySandboxDemoAsync` as a `Func<Task>`.
- **Hotkey events:** `_hotkey.TextMode` (EventHandler), `_hotkey.DictateEnded` (`ShortcutEndedEventArgs.Cancelled`) and `_hotkey.HandsFreeToggled`.
- **Stopping:** `CancelTask()` cancels the running task; `_taskCts` is non-null while one runs.
- **Opening:** `_shell.OpenNotch(NotchTab.Home)` opens Home; `OpenUrl(url)` launches a URL.
- **Fixtures:** `tests/fixtures/shell-enums.v1.json` is shared with other phases, so the onboarding lists get their own fixture, `tests/fixtures/onboarding.v1.json`.

## Task map

| Task | Title | Ledger | Model |
|---|---|---|---|
| H1 | Onboarding contract, flow and controller routing (Core) | A11 | sonnet |
| H2 | Offline draw-demo reply (Core) | A11 | sonnet |
| H3 | Tour card in the shell page + Settings "Welcome tour" row | A11 | opus |
| H4 | App wiring: first run, actions, demos, events | A11 | opus |
| H5 | Native acceptance | A11 | (controller) |

Order:
- H1 and H2 touch disjoint files and may run in parallel.
- H3 needs H1's fixture and names; H4 needs H1 and H2.
- H3 and H4 touch disjoint files and may run in parallel.
- H5 runs last.

## Skip rows (recorded in the ledger with these reasons)

| Item | Reason |
|---|---|
| "How did you hear about us" chips, skill selection, squad packs, hatch flight, intro video, tour music | Marketing and telemetry, or art and sound we would have to invent wholesale. The spec's intro is welcome + colour only. |
| Sign-in step | OpenClicky keeps ChatGPT / OpenRouter in Settings → Account, and AGENTS.md forbids automating sign-in. Voice hello links to Account when no provider is set. |
| Speaker check ("set volume to 50%") | It changes a Windows setting on the user's behalf. B's muted-speaker card already covers silence. |
| Interview that builds personalised agents, starter cast | Not in the spec. It would send an interview to the provider. E owns companions. The finale only opens Home. |
| Email-draft step | Not in the spec, and its behaviour is unknown. |
| Paywall, invite surface | OpenClicky has no plans. |
| Clickable progress dots ("go to step n"), resume mid-tour after a restart | The dots show progress only. A tour quit half-way starts again next launch; skip is one click. |

## Global Constraints

- Everything from sub-projects A, S, B, C and D.
- **Clean room:** match behaviour and order only. All copy in this plan is our own; use it exactly. Never copy HeyClicky code, wording, art, sounds or fonts.
- **AGENTS.md:**
  - Confirm-each is the default access mode. The drive demo always runs Confirm-each, whatever the setting, and never Auto or Full.
  - Never enable Full or Auto on the user's behalf. Consent never changes the access mode.
  - Never automate sign-in, credential entry or Windows security/privacy changes. Permission cards only explain and open `ms-settings:` pages; the user decides.
  - Explain which screens and audio leave the machine (copy below).
  - The tour can be skipped from every step and replayed from Settings → General.
  - It never runs unattended actions: each demo starts only on the user's click, and the drive demo also asks for consent and confirms each step. Skip or Finish while a demo runs stops it.
- **First-run flag:** `FirstRunCompleted` becomes `true` only on Skip or Finish. Tests use temp settings and never touch the user's real settings file.
- **Merge friendliness:** new code in new files.
  - Edits to `ShellContract.cs`, `ShellController.cs`, `IShellHost.cs`, `contract.ts`, `Notch.tsx`, `TrayApplicationContext.cs`, `TrayApplicationContext.Shell.cs` and `ShellSurfaceAdapter.cs` are small, additive and never reformat.
  - Do not edit `tests/fixtures/shell-state.v1.json` or `shell-enums.v1.json`.
- **Wire names:**
  - `ShellState.onboarding` is omitted when no tour runs.
  - Steps: `welcome, colour, microphone, screen, micCheck, voiceHello, drawDemo, circleToAsk, driveDemo, textMode, dictation, finale`.
  - Actions: `start, next, back, skip, finish, recheck, openScreenSettings, drawDemo, driveDemo`.
- **Screen settings URI:** `ms-settings:privacy-graphicscaptureprogrammatic` (fixed in the host, never from the page).
- **Build gate:**
  - `dotnet build OpenClicky.sln -c Release --no-restore` with 0 warnings;
  - `dotnet test tests/OpenClicky.Core.Tests --no-restore` green;
  - `dotnet test tests/OpenClicky.Input.Tests --no-restore` green;
  - `pnpm --dir apps/companion-workspace test` green.
- **Dev environment:**
  - set `TEMP=TMP=D:\tmp-build`, `NUGET_PACKAGES=D:\tmp-build\nuget-packages` and `NUGET_HTTP_CACHE_PATH=D:\tmp-build\nuget-http`;
  - never restore to C:;
  - never kill a running OpenClicky;
  - never click or type into the user's apps.
- **Commits:** explicit paths only. End each message with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

- **Skip during a demo.** Skip or Finish while the drive or draw demo runs must stop it (the task is cancelled, the turn is cancelled, the drawing is cleared) and end the tour. Nothing may keep clicking after the card is gone. Tests: `OnboardingFlowTests.EndClearsBusy`; H4 wiring is reviewed against "EndTour cancels".
- **Access snapshot.** With Settings set to Auto or Full, the drive demo still runs Confirm-each with the consent card, and the setting is unchanged afterwards. Pinned by reusing `_sandboxDemo` (which forces ConfirmEach in `StartAgentTask`); H5 user check 6.
- **Double clicks and stale actions.** A second `drawDemo`/`driveDemo` while one runs, `next`/`back` while busy, any action while no tour runs, and `start` while a tour runs are all no-ops. Tests: `OnboardingFlowTests.NextAndBackBlockedWhileBusy`, `ActionsIgnoredWhenInactive`, plus the H4 guards (`start` ignored while `Active`, demo guards).
- **The page sends junk.** Unknown onboarding actions, a missing action and a non-string action are rejected by the parser. Tests: `OnboardingContractTests.ParserAcceptsOnlyKnownActions`.
- **Draw demo out of bounds on small or odd screens.** Every drawn point must lie inside the screenshot, so nothing is silently dropped. Tests: `OnboardingDrawDemoTests.AllPointsInsideScreenshot` (1280×720, 1920×1080, 3840×2160, 1080×1920 portrait).

---

### Task H1: Onboarding contract, flow and controller routing (Core)

**Files:**
- Create: `apps/windows/OpenClicky.Core/Shell/OnboardingContract.cs`, `apps/windows/OpenClicky.Core/Onboarding/OnboardingFlow.cs`, `tests/fixtures/onboarding.v1.json`
- Modify (additive, a few lines each): `apps/windows/OpenClicky.Core/Shell/ShellContract.cs` (the `ShellState` body property and one `IsValid` case), `apps/windows/OpenClicky.Core/Shell/IShellHost.cs` (a default method), `apps/windows/OpenClicky.Core/Shell/ShellController.cs` (field, `SetOnboarding`, apply in `PushNow`, route in `OnMessage`)
- Test: `tests/OpenClicky.Core.Tests/OnboardingFlowTests.cs`, `tests/OpenClicky.Core.Tests/OnboardingContractTests.cs`

**Interfaces (Produces):**
- `namespace OpenClicky.Core.Shell`:
  - `public enum OnboardingStep { Welcome, Colour, Microphone, Screen, MicCheck, VoiceHello, DrawDemo, CircleToAsk, DriveDemo, TextMode, Dictation, Finale }`.
  - `public sealed record OnboardingView(OnboardingStep Step, int Index, int Count, bool StepDone, bool Busy)`.
  - `public static class OnboardingContract { public static readonly IReadOnlyList<string> Actions = ["start","next","back","skip","finish","recheck","openScreenSettings","drawDemo","driveDemo"]; }`.
- `ShellState` body: `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public OnboardingView? Onboarding { get; init; }`.
- `ShellIntentParser.IsValid`: `"onboarding" => OneOf(m, "action", OnboardingContract.Actions)`.
- `IShellHost`: `void OnboardingAction(string action) { }`, an allow-listed action.
- `ShellController`:
  - `public void SetOnboarding(OnboardingView? view)` stores the view and `RequestPush()`es;
  - `PushNow` posts `ShellStateBuilder.Build(inputs) with { Onboarding = _onboarding }`;
  - `OnMessage` case `"onboarding"` → `_host.OnboardingAction(intent.GetString("action")!)`.
- `namespace OpenClicky.Core.Onboarding`, `public sealed class OnboardingFlow`:
  - `static IReadOnlyList<OnboardingStep> Steps` (all 12, enum order);
  - `bool Active`, `OnboardingStep Step`, `bool StepDone`, `bool Busy { get; set; }` (setting it while inactive does nothing);
  - `void Start()`: Active, Welcome, not done, not busy;
  - `bool Next()`: false when inactive, busy or on Finale; otherwise the next step, with StepDone false;
  - `bool Back()`: false when inactive, busy or on Welcome;
  - `bool MarkDone(OnboardingStep step)`: true only when active, `step == Step` and not already done;
  - `void End()`: inactive, not busy;
  - `OnboardingView? View`: null when inactive, else `(Step, index, 12, StepDone, Busy)`.
- Fixture `tests/fixtures/onboarding.v1.json`:
  ```json
  { "steps": ["welcome","colour","microphone","screen","micCheck","voiceHello","drawDemo","circleToAsk","driveDemo","textMode","dictation","finale"],
    "actions": ["start","next","back","skip","finish","recheck","openScreenSettings","drawDemo","driveDemo"] }
  ```

- [ ] **Step 1: Write the failing tests.**
  - `OnboardingFlowTests`:
    - `StartsInactiveWithNoView`;
    - `StartShowsWelcomeOfTwelve`: View is `(Welcome, 0, 12, false, false)`;
    - `NextWalksAllStepsInOrderThenStopsAtFinale`: 11 Nexts reach Finale; a 12th returns false;
    - `BackStopsAtWelcome`;
    - `NextAndBackBlockedWhileBusy`;
    - `MarkDoneOnlyForCurrentStep`: MarkDone(TextMode) on Welcome is false; on TextMode it is true, then false again;
    - `MovingResetsStepDone`;
    - `ActionsIgnoredWhenInactive`: Next/Back/MarkDone are false; setting Busy leaves View null;
    - `StartResetsToWelcome`: `Start()` after two Nexts returns to Welcome (the "start while a tour runs is ignored" rule is H4's guard on `Active`);
    - `EndClearsBusy`.
  - `OnboardingContractTests`:
    - `StepsAndActionsMatchSharedList` reads `tests/fixtures/onboarding.v1.json` (find the repo root as `ShellContractTests` does), comparing the camelCase wire names of `OnboardingStep` and `OnboardingContract.Actions`;
    - `ParserAcceptsOnlyKnownActions`: `{"kind":"onboarding","action":"next"}` is accepted; `"action":"nuke"`, no action, and `"action":1` are rejected;
    - `StateOmitsOnboardingWhenNull` (the serialized state has no `"onboarding"` key) and `StateCarriesOnboarding` (`"onboarding":{"step":"driveDemo","index":8,"count":12,"stepDone":true,"busy":false}`), both using `ShellContract.Serialize` on a state from `ShellStateBuilder.Build`;
    - `ControllerRoutesOnboardingAndPushesView`: use a fake host/surface. Find and reuse an existing fake in `ShellControllerTests.cs` if there is one, otherwise write a minimal one. Posting the onboarding intent calls `OnboardingAction("skip")`; after `SetOnboarding(view)` the next posted state contains it.
- [ ] **Step 2: Run them and see them fail.** `dotnet test tests/OpenClicky.Core.Tests --no-restore --filter "FullyQualifiedName~Onboarding"`.
- [ ] **Step 3: Implement** the types, the flow and the four small edits.
- [ ] **Step 4: Run the tests and see them pass**, plus the full Core suite (`ShellContractTests` with the existing fixtures stays green).
- [ ] **Step 5: Commit** `feat(onboarding): tour steps, flow and shell routing`.

### Task H2: Offline draw-demo reply (Core)

**Files:**
- Create: `apps/windows/OpenClicky.Core/Onboarding/OnboardingDrawDemo.cs`
- Test: `tests/OpenClicky.Core.Tests/OnboardingDrawDemoTests.cs`

**Interfaces (Produces):**
- `public static class OnboardingDrawDemo`:
  - `public const string Prompt = "Show me how you draw on my screen";`
  - `public const string Spoken = "I can circle things, point at them and leave you a note. Press Escape and it all goes away.";`
  - `public static string Reply(IReadOnlyList<ScreenCaptureResult> screens)`.
- `public sealed class OnboardingDrawTalkClient : ITalkClient`: `CompleteVisionTalkAsync` returns `OnboardingDrawDemo.Reply(screens)` and never touches the network.
- **Reply rules:**
  - **Target:** the cursor monitor, or the first screen. N is its 1-based index in `screens`. Add `screen=N` only when N > 1. With no screens, use 1920×1080.
  - **W×H** = `PixelWidth`×`PixelHeight`, and m = min(W, H).
  - **Shapes, in order, all integers:**
    1. `[DRAW:circle id=demo-circle x={W/2} y={H/2} r={m/10} label="I circle things"]` at the start of the sentence;
    2. `[DRAW:arrow id=demo-arrow pts={W/4},{H*3/4},{W/2 - m/10 - m/40},{H/2 + m/40}]` before "point at them";
    3. `[DRAW:sticky id=demo-note x={W*3/4} y={H/4} text="Hi! I'm OpenClicky." stock=butter]` before "leave you a note";
    4. end with ` [POINT:none]`.
  - `DrawCommandParser.Parse(reply).Text` must equal `Spoken`.

- [ ] **Step 1: Write the failing tests** `OnboardingDrawDemoTests`:
  - `ReplyParsesToThreeShapesAndSpokenText`: Ops are circle, arrow, sticky; Text == `Spoken` once `[POINT:none]` is stripped. Strip it the same way the talk pipeline does; `PointParser` is in Core, so find its API.
  - `AllPointsInsideScreenshot` for 1280×720, 1920×1080, 3840×2160 and 1080×1920: every op's Points/Box lie within [0, W]×[0, H], and the circle's box is inside too.
  - `SecondScreenUsesScreenIndex`: two screens, the second with `IsCursorMonitor`; every op has `Screen == 2`.
  - `NoScreensFallsBack`.
  - `ClientReturnsReply`.
  - Build `ScreenCaptureResult` the way existing tests do; grep `new ScreenCaptureResult(` under `tests/`.
- [ ] **Step 2: Run them and see them fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the tests and see them pass**, plus the full Core suite.
- [ ] **Step 5: Commit** `feat(onboarding): offline draw demo reply`.

### Task H3: Tour card in the shell page + Settings "Welcome tour" row

**Files:**
- Create: `apps/companion-workspace/src/shell/onboarding/Onboarding.tsx`, `onboarding/steps.tsx`, `onboarding/onboarding.css`, `onboarding/Onboarding.test.tsx`
- Modify:
  - `src/shell/contract.ts`: the `onboardingSteps`/`onboardingActions` consts, `onboarding: …optional()` in `shellStateSchema`, and the `PageMessage` member `{ kind: "onboarding"; action: OnboardingAction }`;
  - `src/shell/notch/Notch.tsx`: render `<Onboarding state={state} />` inside `.notch-shell` after the notch div and before `<NotchCards>` when `state.onboarding` is set;
  - `settings/MicrophonePage.tsx`: export `useMicLevel`;
  - `settings/CursorPage.tsx`: export `GlowingCursor`;
  - `settings/GeneralPage.tsx`: a "Welcome tour" group.
- Test: `onboarding/Onboarding.test.tsx` (reads `tests/fixtures/onboarding.v1.json` and `shell-state.v1.json`) and `settings/settings.test.tsx` (one test for the replay row).

**Interfaces:**
- Consumes the wire names from H1. The page posts `{kind:"onboarding", action}` and the existing `settings.patch`, `settings.action` and `notch.tab`/`settings.section`.
- Produces:
  - `export function Onboarding({ state }: { state: ShellState })`, which returns null without `state.onboarding`;
  - root `<section className="onboarding" data-region role="dialog" aria-label="OpenClicky tour">`.
- **Zod schema:** `onboarding: z.object({ step: z.enum(onboardingSteps), index: z.number().int().min(0).max(11), count: z.literal(12), stepDone: z.boolean(), busy: z.boolean() }).strict().optional()`.

**Chrome (every step):**
- Progress dots: 12 dots in a `role="progressbar"` with `aria-valuenow={index+1}`, `aria-valuemax={12}` and `aria-label="Step {index+1} of 12"`; the current dot is lit.
- Title (`h2`) and body.
- Footer:
  - **Back** (secondary): hidden on Welcome.
  - **Continue** (primary): action `next`. Not on Finale.
  - **Skip tour** (ghost): action `skip`. Not on Finale.
- While `busy`, Back and Continue are disabled; Skip stays enabled.
- Width 440 px; follows `--accent`, theme and reduced motion like the cards.

**Steps.** Exact copy. `{talk}`, `{text}`, `{dictate}` and `{handsFree}` render as `KeyCaps` from `state.hotkeys`. `{talkVerb}` is `talkGesture(state)` ("Hold" / "Double-tap"); for text and dictate the verb comes from their `settings.shortcuts.*.trigger` the same way.

1. **welcome**
   - Title: "Hi, I'm OpenClicky."
   - Body: "I live at the top of your screen and help with whatever you're looking at. Talk to me, type to me, or let me show you things right on your screen. The tour takes about three minutes, and you can skip it at any point."
2. **colour**
   - Title: "Pick my colour"
   - Body: "It's the colour of my cursor and of everything I draw. You can change it later in Settings → Cursor."
   - Four `Tile`s from `ACCENTS` with `GlowingCursor`; the selected one matches `settings.cursor.colour`; a click posts `settings.patch cursor.colour <hex>`.
3. **microphone**
   - Badge: "Permission 1 of 2"
   - Title: "Let me hear you"
   - Body: "I only listen after you press the talk keys, start a conversation or dictate. Windows decides whether desktop apps may use the microphone, and you choose there."
   - Status line, from the reach item with id `microphone`:
     - allowed: "Windows lets OpenClicky use the microphone."
     - denied: "Windows is blocking desktop apps from the microphone. Turn on \u201cLet desktop apps access your microphone\u201d, then check again."
     - unknown, or missing: "I couldn't read the Windows setting. If talking doesn't work, check it there."
   - Privacy line: "Dictation always turns speech into text on this PC. So does Talk with ChatGPT or Windows speech; with an OpenRouter key and Windows speech off, the recording goes to OpenRouter."
   - Buttons:
     - **Open microphone settings**: `settings.action openPrivacySetting`, arg `microphone`;
     - **Check again**: `onboarding recheck`.
4. **screen**
   - Badge: "Permission 2 of 2"
   - Title: "Let me see what you see"
   - Body: "Windows doesn't ask before desktop apps take screenshots, so there is nothing to switch on. I take them only when you talk to me or a task runs: up to four displays, with my own windows left out. They go with your words to your model provider (ChatGPT or OpenRouter) and nowhere else."
   - Button: **Open Windows screen settings**: `onboarding openScreenSettings`.
5. **micCheck**
   - Title: "First, can I hear you?"
   - Body: "Press Test and say something. The bars move when I can hear you. The test stays on this PC."
   - Microphone line: "Listening with {device name}". The name is the `settings.microphone.devices` entry whose id is `deviceId`, else "the Windows default microphone".
   - Meter: 16 bars, segments, label "Microphone level", fed by `useMicLevel()`.
   - Buttons:
     - **Test microphone**: `settings.action testMic`;
     - **Change microphone**: `notch.tab settings` then `settings.section microphone`.
   - Done (page-side): once a level ≥ 0.15 has been seen while on this step, show "✓ I can hear you."
6. **voiceHello**
   - Title: "This is how you talk to me"
   - Body: "{talkVerb} {talk}, say \u201cHi, I'm …\u201d with your name, then let go. I'll answer out loud."
   - Status from `status.phase`:
     - listening: "Listening…"
     - thinking: "Thinking…"
     - speaking: "Speaking…"
     - else: "Waiting for you…"
     - when `stepDone`: "✓ Nice to meet you!"
   - When `!signedInAs && !openRouterKeySet`: note "Connect ChatGPT or add an OpenRouter key first, so I can answer." with the button **Open Account settings** (`notch.tab settings`, then `settings.section account`).
   - Privacy: "Your words and screenshots go to your model provider."
7. **drawDemo**
   - Title: "I can draw on your screen"
   - Body: "When it helps, I sketch right over your apps: circles, arrows and notes that never change the app underneath. This sample runs on this PC; nothing from your screen is sent."
   - Button: **Show me** (`onboarding drawDemo`), disabled while busy.
   - When `stepDone`: "✓ Press Esc to wipe it."
8. **circleToAsk**
   - Title: "Circle anything, then ask"
   - Body: "{talkVerb} {talk} and, while you talk, circle or underline something with your mouse. Ask \u201cWhat's this?\u201d and I'll know what you mean."
   - When `!settings.cursor.screenHighlight`: note "Screen highlight is off." with the button **Turn it on** (`settings.patch cursor.screenHighlight true`).
   - Privacy: "Your marks, the text under them and the screenshot go with the question to your model provider."
   - When `stepDone`: "✓ Got it."
9. **driveDemo**
   - Title: "I can use your mouse and keyboard"
   - Body: "For bigger jobs I can click and type for you, one step at a time. Practise on a test window: nothing else on your PC is touched and no model is called."
   - Checklist (all ticked when `stepDone`): "Click the green button", "Click into the text box", "Type hello".
   - Note: "I ask before I start and before every step, whatever your access setting. Move the mouse or press a key to take over; Esc stops."
   - Button: **Start the practice run** (`onboarding driveDemo`), disabled while busy.
   - When `stepDone`: "✓ All three done, checked in the test window."
10. **textMode**
    - Title: "Rather type?"
    - Body: "{textVerb} {text} and a text box opens up here. Type your question and press Enter."
    - When `stepDone`: "✓ That's the one."
11. **dictation**
    - Title: "Dictate into any app"
    - Body: "Click into a text box in another app, such as Notepad. {dictateVerb} {dictate}, speak, then let go: your words appear at the cursor. {handsFreeVerb} {handsFree} to keep dictating hands-free."
    - Privacy: "Your voice becomes text on this PC. With Tidy up with AI on, the words, the app name and the text around the cursor go to your model provider."
    - When `settings.microphone.whisper !== "ready"`: note "Dictation needs the local voice. Download it in Settings → Microphone." with the button **Open Microphone settings**.
    - When `stepDone`: "✓ Nicely done."
12. **finale**
    - Title: "You're all set"
    - Body: "Hover the bar at the top of your screen for Home, Chat and Settings. You can take this tour again from Settings → General."
    - Button: **Show me Home** (`onboarding finish`) instead of Continue/Skip; Back stays.

**General page:** add a group titled "Welcome tour" with a row titled "Replay the welcome tour", description "Walk through talking, drawing, dictation and the practice run again.", and the button **Start tour** (posts `onboarding start`, then `notch.open false`: the tour card is hidden while the notch is open on Settings).

- [ ] **Step 1: Write the failing tests** (`Onboarding.test.tsx`; mock the host as `notch-extras.test.tsx` does):
  - `contract lists match the shared onboarding fixture` (`onboardingSteps`, `onboardingActions`);
  - `parseShellState accepts a state with onboarding and the fixture without it`; an unknown step is rejected;
  - `renders nothing without onboarding`;
  - `welcome shows step 1 of 12, no Back, and Continue posts next`;
  - `Skip tour posts skip on every step except the finale`;
  - `busy disables Back and Continue but not Skip`;
  - `colour tiles patch cursor.colour and mark the current colour`;
  - `microphone card shows the denied copy and opens the microphone page` (posts `settings.action` with `action: "openPrivacySetting", arg: "microphone"`);
  - `screen card opens Windows screen settings` (posts `onboarding openScreenSettings`);
  - `mic check ticks after a loud enough level` (dispatch a host `mic.level` 0.3 through the mocked listener);
  - `voice hello shows the talk keys and the account note without a provider`;
  - `drive demo lists the three steps and ticks them when done`;
  - `finale's Show me Home posts finish`;
  - `the tour card is a reported region inside the notch shell` (a rendered `Notch` with onboarding has `.notch-shell .onboarding[data-region]`);
  - in `settings.test.tsx`, `General page Start tour posts onboarding start`.
- [ ] **Step 2: Run them and see them fail.** `pnpm --dir apps/companion-workspace test -- onboarding settings`.
- [ ] **Step 3: Implement.** Reuse `PillButton`, `Tile`, `Meter`, `KeyCaps`, `Badge` and `CheckIcon` from `../ui`, and the `act`/`patch` helpers from `settings/common`.
- [ ] **Step 4: Run the tests and see them pass**, plus the full vitest suite and the TypeScript build used by the package's test script (`pnpm --dir apps/companion-workspace exec tsc -p tsconfig.app.json --noEmit` if that is how other phases checked types).
- [ ] **Step 5: Commit** `feat(onboarding): tour card in the notch and replay from General`.

### Task H4: App wiring: first run, actions, demos, events

**Files:**
- Create: `apps/windows/OpenClicky.App/Onboarding/TrayApplicationContext.Onboarding.cs`
- Modify (small):
  - `TrayApplicationContext.cs`:
    - remove `MaybeShowFirstRunTip` and its call;
    - talk factory `_demoTalk ? (_demoTalkClient ?? new OfflineTalkClient()) : …`;
    - split `TrySandboxDemoAsync()` into `=> RunSandboxDemoAsync(showClassic: true)` plus `private async Task<bool> RunSandboxDemoAsync(bool showClassic)`, which returns `_sandbox?.HasVerifiedResult == true` and calls `ShowClassic()` only when `showClassic`;
  - `Shell/TrayApplicationContext.Shell.cs`: one call, `MaybeStartOnboarding();`, inside `if (started)`;
  - `Shell/ShellSurfaceAdapter.cs`: add `"onboarding"` to `PageKinds`.

**Interfaces:**
- Consumes `OnboardingFlow`, `OnboardingView`, `OnboardingStep`, `IShellHost.OnboardingAction` and `ShellController.SetOnboarding` (H1), plus `OnboardingDrawDemo.Prompt` and `OnboardingDrawTalkClient` (H2).
- Produces, in the partial:
  - `private readonly OnboardingFlow _onboarding = new();`
  - `private ITalkClient? _demoTalkClient;`
  - `private void MaybeStartOnboarding()`
  - `void IShellHost.OnboardingAction(string action)`
  - `private void StartTour()`
  - `private void EndTour(bool openHome)`
  - `private void PushTour()`, which calls `_shell?.SetOnboarding(_onboarding.View)`
  - `private const string ScreenSettingsUri = "ms-settings:privacy-graphicscaptureprogrammatic";`
  - `private void WireOnboarding()`, which subscribes the events once. Call it from `MaybeStartOnboarding`'s first run, or from the shell-start site; keep the subscription idempotent.

**Behaviour (pinned):**
- **First run:** `MaybeStartOnboarding` starts the tour only when `!_settings.FirstRunCompleted` and the shell is active.
- **Actions** run on the UI thread (`OnUi`); anything unknown is logged and ignored.
  - **`start`:** ignored while `_onboarding.Active`; else `StartTour()`.
  - **`next` / `back`:** move the flow. On entering `Microphone`, call `SettingsProbe.Refresh()`. On leaving `MicCheck`, stop the mic test with the existing `StopMicTest()`.
  - **`recheck`:** `SettingsProbe.Refresh()`.
  - **`openScreenSettings`:** `OpenUrl(ScreenSettingsUri)`.
  - **`drawDemo`:** only on `DrawDemo`, not busy, `_talk.State == Idle` and no task. Then:
    - set Busy;
    - set `_demoTalkClient = new OnboardingDrawTalkClient()` and `_demoTalk = true`;
    - `await _talk.SubmitTextAsync(OnboardingDrawDemo.Prompt, remember: false)`;
    - in `finally`, reset both fields and Busy, and `MarkDone(DrawDemo)` unless the tour ended meanwhile.
  - **`driveDemo`:** only on `DriveDemo`, not busy, idle, with `_taskCts` null. Then:
    - set Busy;
    - `var ok = await RunSandboxDemoAsync(showClassic: false)`;
    - in `finally`, clear Busy, and `MarkDone(DriveDemo)` when ok and still on that step.
  - **`skip`:** `EndTour(false)`. **`finish`:** `EndTour(true)`.
- **`EndTour(openHome)`:**
  - if a tour demo is running: `CancelTask()` when the sandbox demo is running, else `_talk.CancelCurrentTurn()`;
  - always `_talk.ClearDrawing()` when the draw demo ran;
  - `StopMicTest()`;
  - `_onboarding.End()`;
  - `_settings.FirstRunCompleted = true` and `SettingsStore.Save(_settings)`;
  - `PushTour()`;
  - if `openHome`, `_shell?.OpenNotch(NotchTab.Home)`.
- **Events:**
  - `_talk.TurnCommitted` → `MarkDone(VoiceHello)` or `MarkDone(CircleToAsk)`, whichever is current;
  - `_hotkey.TextMode` → `MarkDone(TextMode)`;
  - `_hotkey.DictateEnded` with `!e.Cancelled`, and `_hotkey.HandsFreeToggled`, → `MarkDone(Dictation)`;
  - every change that returns true → `PushTour()`.
- **Never:** change `_settings.AgentAccess`, touch any other privacy or Windows setting, or start a demo without its click.

- [ ] **Step 1:** There is no unit-testable logic left here (H1 and H2 hold it). Re-read `RunManagedTaskAsync` / `StartAgentTask` to confirm `_sandboxDemo` forces `ConfirmEach` and the C12 consent path, and record that in the commit body.
- [ ] **Step 2: Implement.**
- [ ] **Step 3: Build and test.** `dotnet build OpenClicky.sln -c Release --no-restore`: 0 warnings. The full Core and Input suites are green.
- [ ] **Step 4: Commit** `feat(onboarding): first-run tour wiring, offline draw and drive demos`.

### Task H5: Native acceptance

- [ ] **Step 1: Build gate.** All four commands from Global Constraints.
- [ ] **Step 2: Render check.**
  - Serve the shell page from the vite dev server (`apps/companion-workspace`, `shell.html`) in the Browser pane.
  - Post a fixture `shell.state` with `onboarding` set for several steps through `window.postMessage`; the DEV bridge accepts same-origin messages.
  - Read the accessibility tree and take screenshots: the tour card renders, the dots read "Step n of 12", and the buttons are there.
  - The Surface smoke serves only its own inline page, so it cannot render the shell; record that ruling.
- [ ] **Step 3: User-only checks** (real mic, voice, hotkeys and drive; put them in the ledger):
  1. With a fresh settings folder, or after Settings → General → Start tour, the tour card appears under the notch; Skip tour hides it, and it does not come back on restart.
  2. The colour pick changes the companion cursor colour at once.
  3. The microphone card's button opens Windows' microphone page; after you toggle desktop-app access there, Check again updates the line.
  4. Mic check: Test microphone moves the bars and ticks.
  5. Voice hello: hold Ctrl+Alt and say your name; the status cycles and the reply is spoken; the step ticks.
  6. Draw demo: Show me draws a circle, an arrow and a note; Esc wipes them.
  7. Circle-to-ask: hold the talk keys, circle something, ask; the answer is about it.
  8. Drive demo with Settings → Access set to Auto: the consent card appears, then a confirm for every step in the test window; afterwards Access is still Auto. Skip during the run stops it.
  9. Text mode: double-tap Ctrl opens the text box and the step ticks.
  10. Dictation: in Notepad, hold Ctrl+Win and speak; the text appears and the step ticks.
  11. Finale: Show me Home opens Home.
- [ ] **Step 4: Records.**
  - Update ledger row A11 (→ Built, with evidence) and add the Skip rows.
  - Add an Onboarding row to `docs/feature-plan.md`.
  - Write `.superpowers/sdd/2026-10-25-subproject-h-onboarding/FINAL-REPORT.md`.

---

## Self-review notes

- **Spec coverage (§H):**
  - welcome and colour → H3 steps 1–2;
  - mic and screen permission cards → H3 steps 3–4, H4 `recheck`/`openScreenSettings`;
  - mic check → H3 5, reusing `testMic`/`mic.level`;
  - voice hello → H3 6 + H4 `TurnCommitted`;
  - draw demo → H2 + H4;
  - circle-to-ask → H3 8 + H4;
  - drive demo → H4 `RunSandboxDemoAsync` (C12 consent, Confirm-each);
  - text mode, dictation → H4 hotkey events;
  - finale → H4 `finish`;
  - skip and replay → H3 chrome + General row, H4;
  - our own copy → this plan.
- **Names used across tasks:** `OnboardingStep`, `OnboardingView`, `OnboardingContract.Actions`, `OnboardingFlow`, `IShellHost.OnboardingAction`, `ShellController.SetOnboarding` (H1); `OnboardingDrawDemo`, `OnboardingDrawTalkClient` (H2); wire names in `onboarding.v1.json`.
- **Proportion:** the plan pins the copy, the wire names, the guards and the safety rules; the bodies are left to the implementers.
