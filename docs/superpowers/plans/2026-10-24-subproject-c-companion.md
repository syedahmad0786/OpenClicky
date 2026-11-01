# Sub-project C — Companion: cursor, pointing, drawing, 3D figures, screen highlight, walkthroughs, saved drawings, drive Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give OpenClicky HeyClicky's on-screen companion:
- a glowing accent-coloured triangle cursor that follows the pointer on a spring, with listening bars, thinking dots, a message bubble and a label bubble, and that can dock into the notch, hide, or be shaken loose and clicked to talk;
- pointing with a dashed glowing click ring;
- a hand-drawn drawing engine (primitives, screen helpers, live edits, timed to speech) plus 3D figures;
- drawing on the screen while holding talk, with each stroke classified and the UI text under it sent with the question;
- walkthroughs that snap to real controls and advance on the user's click;
- saved drawings with replay and print;
- "drive" (computer use) behind a consent card, an Esc pill and a take-back watch, on top of OpenClicky's access modes.

**Architecture:**
- All pure logic lives in `OpenClicky.Core` (new folders `Companion/` and `Drawing/`), fully unit-tested: spring and flight motion, shake detection, bubble and ring layout, the `[DRAW:…]` parser, the drawing document (parts, ids, edits, undo, description), speech cueing, ink geometry, figure meshes, stroke classification, the walkthrough target watch, and the drive session.
- `OpenClicky.Overlay` is rewritten as a thin native renderer:
  - per-pixel-alpha layered windows (`UpdateLayeredWindow`) replace the black `TransparencyKey` form: a small cursor window that moves with the spring, and one virtual-screen drawing window that repaints only while something animates;
  - small click-catcher windows serve the detached cursor and drawing buttons;
  - a WPF `Viewport3D` window shows each 3D figure.
- The React shell supplies the cards (save offer, drive consent, Esc pill, walkthrough step) and the drawing rows in Chat.
- New tray wiring goes in partial files under `OpenClicky.App/Companion/`, not in the 1,700-line `TrayApplicationContext.cs`.

**Tech Stack:** .NET 8 WinForms + GDI+ (2D overlay), WPF `System.Windows.Media.Media3D` (3D figures; it renders through Direct3D, and WPF is already a framework reference of `OpenClicky.Agent`), `System.Windows.Automation` (UIA), Win32 P/Invoke (`UpdateLayeredWindow`, `GetCursorPos`, `WindowFromPoint`, `OpenProcessToken`/`GetTokenInformation`), React + zod for the shell. **No new NuGet packages.**

**Spec:** [design spec](../specs/2026-10-24-heyclicky-parity-design.md) §C · ledger rows C1–C8 ([feature-ledger.md](../../heyclicky-parity/feature-ledger.md)) · behaviour evidence in [ui-inventory §5](../../research/heyclicky-1.1.1/ui-inventory.md) and [voice-inventory §4–5](../../research/heyclicky-1.1.1/voice-inventory.md).

## Current code facts (from the survey)

- **Overlay today:** `BuddyOverlayController` (Overlay, about 835 lines) implements `IOverlayController`.
  - Window: one WinForms form covering the virtual screen. It uses `TransparencyKey = Black`, so there is no partial alpha and therefore no glow, no 55 % highlighter and no soft fill. A 33 ms timer drives it.
  - Look: a mint "face" buddy at cursor + (28, 20), a pulse, an 18-point fading trail, and a nearly invisible 24 px `BuddyTipForm` that exists only so smoke tests can assert the tip position.
  - Modes: `Hidden/Listening/Processing/Responding/Pointing`. Fly-to is an ease-out cubic advancing 0.06 per tick.
  - Marks: `DrawMarks` draws `VirtualAnnotation`s with `PartialStroke` progressive reveal (350 ms each, one after another). Bubbles are placed by `ResponseBubblePlacement.Place` to avoid marks.
  - Lifetime: the overlay is shown only during a turn. `Hide()` clears marks and pointing, then hides the form.
- **`IOverlayController` (Core):** `ShowListening`, `ShowProcessing`, `ShowResponding(text)`, `ShowTranscript`, `ShowAnnotations(marks)`, `PointAt(x, y, label, spokenText)`, `SelectRegionAsync`, `Hide`. Default interface members are already used for optional members, so fakes keep compiling.
- **Tags today (Core):**
  - `PointParser` handles `[POINT:x,y:label:screenN]`. `ScreenAnnotations.Parse/Map` handles `[MARK:shape:coords:label:screenN]`: 7 shapes (rect, circle, arrow, polygon, freehand, curve, text), at most 6.
  - `CoordinateMapper.ToVirtualScreen` converts screenshot pixels (top-left origin, cursor monitor by default) to virtual pixels. There is no reverse mapping yet.
- **`TalkOrchestrator.RunTurnAsync`:**
  - Parse order: `TalkActionParser` → `IntelligentUiParser` → `VisualArtifactReply` → `ScreenAnnotations` → `PointParser`.
  - Speech: streamed through `StreamingReplySpeech`, where a sentence ends at `.`, `!` or `?` followed by whitespace or the end of the text; `Clean(raw)` strips tags, including a dangling partial one. Speech is queued per chunk via `SpeakAfterAsync`.
  - Marks are shown only after the whole reply arrives. With speech off the turn waits 6,000 or 1,800 ms; it also waits up to 8 s after speech while marks are shown. The `finally` calls `_overlay.Hide()`.
  - The overlay is hidden before capture.
  - Events: `WalkthroughChanged`, `ActionsRequested`, `HeldTalkStarted`, `StateChanged`.
- **Walkthrough today:**
  - `GuidedWalkthrough` (Core) accepts `[GUIDE:step|wait|complete]` replies and moves Next/Back/Refresh from the classic `WalkthroughForm` buttons or by voice ("next step", "previous step", "recheck step", "stop walkthrough"). It keeps at most 8 history entries.
  - It has no step cap, no target, no click watch and no UIA snap. Its `VisualTags` regex strips `[POINT:` and `[MARK:` from instructions.
  - "show me how…" or "guide me…" starts one. `WalkthroughTargetWindow.Track/MustRestore` tracks the target app.
- **3D today:** the ```` ```artifact ```` block with `kind: "scene3d"` (cube or pyramid) renders in the WebView workspace (`VisualArtifactReply`, `artifacts.tsx`). It is an OpenClicky extra and is kept unchanged. Nothing uses Direct3D or Vortice.
- **Agents:**
  - Two paths: the native `AgentOrchestrator`, and OpenWork through `WindowsDesktopToolHost` + `DesktopToolSession`.
  - Both execute through `TrayApplicationContext.ExecuteMappedAsync` → `SendInputExecutor`.
  - The access mode is snapshotted once in `StartAgentTask` (`var access = … _settings.AgentAccess`), and `OnSettingsApplied` cancels tasks.
  - `AgentAccessPolicy.RequiresConfirmation` decides per action, and Preview stops before any mutation. Auto shows a `ConfirmTaskAsync` plan approval.
  - `WinFormsAgentConfirm` (with `IsDialogOpen`) is the per-action Yes/No dialog.
- **Physical takeover:**
  - `PhysicalUserActivityMonitor` (Input, Raw Input) ignores injected input, because injected events have device handle 0.
  - Any physical event calls `PauseTaskUnlessConfirming()`, skipped while the confirm dialog is open. Screen-changing input also cancels a responding turn.
  - Nothing reads process integrity or elevation anywhere.
- **Esc:** `_hotkey.EscapePressed += … { _tts.Stop(); _shell?.ClearCaptions(); if (IsBusy) Stop(); }`. `IsBusy` covers a task, a turn, dictation, a conversation and a walkthrough.
- **Capture:**
  - Talk uses `OwnWindowsExcludedCapture` with `OwnWindowsForCapture()` (`TrayApplicationContext.Capture.cs`), which enumerates every visible window of the process plus `_overlay.WindowHandle`/`_overlay.BuddyTipHandle`. `ApplyCaptureExclusion()` in `Shell/TrayApplicationContext.Shell.cs` reads the same two handles.
  - The agent captures regions directly and calls `_overlay.Hide()` first.
  - `CaptureImageEncoder.BitmapToJpeg(Bitmap, maxEdge = 1280)` exists in the Capture project.
- **Settings:**
  - Existing: `AppSettings.AccentColor` (`#3380FF`, snapped by `SettingsCatalog.SnapAccent` to `SettingsCatalog.Accents`), `CursorHidden`, `ScreenHighlight` (default true), `ShakeToDetach` (default true), `Paused`, `SpeakReplies`, `SpeechSpeed`.
  - Patch keys `cursor.colour/hidden/screenHighlight/shakeToDetach` and the Settings → Cursor page exist from S. **No App or Overlay code reads the cursor settings yet.**
  - `GeneralBehaviour.ReducedMotion()` exists.
- **Shell:**
  - Cards: `NotchCardKind` (`activity, textResponse, jobUpdate, needsYou, mutedSpeaker, micTrouble, clipboard, dictionaryLearned, documentAccess, appUpdated, tip, toast`). `ShellController.AddCard/RemoveCard`. `IShellHost.CardAction(cardId, actionId)` returns true to keep the card. Card factories follow the `TalkCards` pattern (`Core/Speech/TalkWarnings.cs`).
  - Chat rows come from `ConversationTurn` history plus `LiveRows` (`ShellStateBuilder`). There are no drawing rows.
  - `contract.ts` mirrors `ShellContract.cs`, and both suites parse `tests/fixtures/shell-state.v1.json`.
- **Mic level:** `Shell/TrayApplicationContext.TalkFeedback.cs` samples `PeakLevel` every 100 ms while listening (mic-trouble watch).
- **Classic dock:** `CompanionDockForm` (a round dock with state and `OfferHelp`) is shown at startup.
- **UIA:** `OpenClicky.Agent` (`UseWPF` + `UseWindowsForms`) already uses `System.Windows.Automation` (`UiaSnapshotService`, `DictationTarget`).
- **Core is `net8.0`.** It has only `System.Drawing.Primitives` and no `System.Drawing.Common`, so new Core geometry uses its own `Pt`/`Box` records (they are also JSON-stable for saved drawings).
- **Smoke:** `OpenClicky.Overlay.Smoke` asserts `PartialStroke`, `ScreenRegionSelector.Normalize`, `ResponseBubblePlacement` and the POINT tip position via `BuddyTipHandle`/`TryGetBuddyVirtualPoint`.

## Task map

| Task | Title | Ledger | Suggested model |
|---|---|---|---|
| C1 | Cursor motion and layout logic (Core) | C1 | Terra |
| C2 | Layered overlay and companion cursor look | C1 | Sol |
| C3 | Dock, hide, shake to detach | C1 | Terra |
| C4 | Pointing and click ring | C2 | Terra |
| C5 | Drawing commands and drawing document (Core) | C3 | Sol |
| C6 | Drawing renderer, ink style, speech pacing | C3 | Sol |
| C7 | Screen helpers and drawing buttons | C3 | Terra |
| C8 | 3D figures | C4 | Sol |
| C9 | Screen highlight gesture | C5 | Sol |
| C10 | Walkthrough targets and click-to-advance | C6 | Sol |
| C11 | Saved drawings: save offer, chat rows, replay, print | C7 | Terra |
| C12 | Drive: consent, Esc pill, take-back, elevation refusal | C8 | Sol (Astra reviews) |
| C13 | Native acceptance | C1–C8 | Terra |

Order: C1 → C2 → C3/C4; C5 → C6 → C7/C8/C11; C9 after C6; C10 after C4 and C6; C12 any time after C2; C13 last.

## Skip rows (recorded in the ledger with these reasons)

| Item | Reason |
|---|---|
| Record a drawing to video (MP4) | It needs Media Foundation sink-writer interop or a new encoder package for one export button. Windows' own screen recorder (Snipping Tool, Win+Shift+R) can capture a replay. Revisit if users ask. |
| Replay beat scrubber | Play/pause, restart and close cover replay. A scrubber adds timeline UI for little value. |
| Holding (blocking) the user's mouse and keyboard during drive | Safety: OpenClicky never blocks the user's input. The take-back watch (C12) hands control back instead. |
| `image`/`photo` (web image search) and the `receipt`/`clipping` paper variants | Image search fetches untrusted web content from model queries. `card`/`sticky` with paper stocks cover the explanations. |
| Drawing sound effects (`sfx`, pen/paper sounds) | We cannot reuse HeyClicky's sounds and have no sound set of our own. Silent drawing is acceptable. |
| `handOff` drawing helper | Belongs to sub-project E (jobs). |
| Pinned/draggable drawing cards that fly into the notch, and drawing suggestion cards | Drawings are click-through. Dragging needs interactive windows, and suggestion cards belong to P. |
| 3D ink outline, part clicks, `blob`/`surface` parts, and a GPU (Vortice/HLSL) renderer | WPF `Viewport3D` already renders through Direct3D with no new package. Outlines, picking and freeform surfaces are polish; add them if figures look flat in side-by-side review. |
| Walkthrough 3 % screen-change tolerance and the "manual action" verb regex | `[GUIDE:wait]` and the next screenshot already cover "not ready yet". |
| Exclusive-fullscreen game note | Rare. Drawings simply do not show over exclusive fullscreen. |
| Welcome bubble on first launch | Belongs to H (onboarding). |

## Global Constraints

- Everything from sub-projects A, S and B.
- **Clean room:** match behaviour, layout and feel only. Never copy HeyClicky code, prompt text, art, sounds, fonts or wording. All copy and prompt text in this plan is our own.
- **Surfaces:** the cursor and drawing overlay stay native; the notch and cards are the React shell.
- **Accent colours:** Blue `#3380FF` (default), Red `#FF4C4C`, Yellow `#D4A017`, Green `#26C281`, read from `AppSettings.AccentColor` via `SettingsCatalog.SnapAccent`. The cursor, rings, marks and pills use the accent unless an op sets `color`.
- **Overlay windows:** layered, per-pixel alpha, `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`, topmost. They never take focus (except the replay Window/Board modes in C11). Sizes are in DIP × the scale of the monitor under the point. All coordinates are virtual-screen physical pixels (the app is PerMonitorV2).
- **Reduced motion** (`GeneralBehaviour.ReducedMotion()`): no springs, flights, spins or pen reveal; everything snaps and fades in 120 ms.
- **Model coordinates:** screenshot pixels with a top-left origin. `screen=N` (DRAW) or `:screenN` (POINT/MARK) selects the monitor; the default is the cursor monitor. Any op with a point outside its screenshot is dropped, never clamped.
- **Esc always stops:** a task or drive, a walkthrough, a replay, and the current drawing (Esc clears it).
- **Safety rules (unchanged):**
  - Confirm-each is the default. Auto and Full need a visible warning.
  - A running task's access snapshot never silently widens. OpenClicky never enables Full on the user's behalf.
  - Drive keeps OpenClicky's access modes: ConfirmEach still asks per action, and Preview never drives.
  - A physical mouse move of ~40 px (or any physical key or click) takes control back.
  - A window whose process integrity is higher than OpenClicky's, or cannot be read, is refused.
  - Title guards and deny lists are conveniences, not a security boundary.
- **Untrusted text:** screen text, UIA text and model-authored drawing labels or button prompts are untrusted data. They are quoted in prompts and never grant permission.
- **What leaves the machine:** the C9 highlight gesture adds the user's strokes composited into that turn's screenshot, plus the UIA text under the strokes, to the request sent to the model provider (the same destination as today's screenshots). Saved drawings stay local and store no screenshots.
- **Tests:** pure logic gets xUnit tests in `tests/OpenClicky.Core.Tests`; shell changes get vitest. **Build gate:** `dotnet build OpenClicky.sln -c Release` with 0 warnings, `dotnet test tests/OpenClicky.Core.Tests` green, `pnpm --dir apps/companion-workspace test` green.

## Review Focus

- Clicking the drive consent card or the Esc-pill card is physical input. It must not pause or take back the task it is approving. Test: `DriveSessionTests.InputWhileAwaitingConsentIsIgnored`.
- While driving, mouse jitter under 40 px must not take control back, but 40 px, a physical click or a key must, and the agent's own cursor moves never count. Tests: `DriveSessionTests.JitterUnder40PxIgnored`, `FortyPxTakesBack`, `ClickOrKeyTakesBack`, `BaselineResetsAfterAgentMove`.
- If the foreground window's integrity cannot be read (an access-denied admin app), drive refuses rather than sending input that Windows silently drops. Test: `DriveGuardTests.UnreadableIntegrityRefuses`.
- During a walkthrough, clicking OpenClicky's own card buttons (Back, Stop), or just inside the padding of the target control, must not end the guide as "clicked elsewhere". Tests: `WalkthroughTargetWatchTests.OwnWindowClickIgnored`, `ClickInsideInflatedFrameAdvances`.
- Malformed model drawing output must never crash, draw off-screen or be spoken. That covers out-of-bounds or huge numbers, unknown ops, an unterminated tag mid-stream, and more than 24 tags. Tests: `DrawCommandParserTests.RejectsMalformedAndOutOfRange`, `DrawingMapperTests.DropsOutOfBounds`, `DrawingCueTrackerTests.PartialTagNeverEmitted`.

---

### Task C1: Cursor motion and layout logic (Core)

**Files:**
- Create: `apps/windows/OpenClicky.Core/Drawing/Geometry.cs` (`Pt`, `Box`).
- Create: `apps/windows/OpenClicky.Core/Companion/CursorMotion.cs` (`CursorConstants`, `CursorSpring`, `FlightPath`, `ShakeDetector`, `CursorShape`, `BubbleLayout`).
- Create: `apps/windows/OpenClicky.Core/Companion/CompanionStyle.cs`.
- Test: `tests/OpenClicky.Core.Tests/CursorMotionTests.cs`, `CompanionStyleTests.cs`.

**Interfaces (Produces):**
- `public readonly record struct Pt(float X, float Y)`; `public readonly record struct Box(float X, float Y, float W, float H)` with `Right`, `Bottom`, `Centre`, `Contains(Pt)`, `Inflate(float)`, `Union(Box)`.
- `CursorConstants`:
  - `TipOffsetX = 35`, `TipOffsetY = 25` (DIP; the tip rests at real cursor + offset);
  - `RestRotationDegrees = -35`, `TriangleSide = 16`, `CornerFraction = 0.09`, `GlowMax = 20`;
  - `Stiffness = 220`, `Damping = 24`;
  - `ReturnCancelDistance = 100`.
- `CursorSpring(Pt start, double stiffness = 220, double damping = 24)`: `Pt Position`, `Pt Velocity`, `Pt Step(Pt target, double dtSeconds)`, `void Snap(Pt p)`. It integrates with semi-implicit Euler in fixed 1/240 s substeps, and dt is clamped to 0.1 s.
- `FlightPath(Pt from, Pt to)`:
  - `double DurationSeconds` = clamp(0.25 + distance / 2400, 0.3, 0.9);
  - `Pt At(double t)`: a quadratic Bézier whose control point is the midpoint offset perpendicular, towards screen-up, by 18 % of the distance, with ease-in-out cubic timing; `t` is clamped to [0, Duration].
- `ShakeDetector`:
  - `bool Add(Pt p, long ms)` returns true once when the last 500 ms contain ≥ 6 direction reversals on the x or y axis (counting only segments ≥ 8 px) and a path length ≥ 260 px;
  - cooldown 1,000 ms.
- `CursorShape.Triangle(Pt tip, float scale, float rotationDegrees) -> Pt[3]`: an equilateral triangle with side `TriangleSide × scale`, the tip vertex at `tip`, pointing up-left at the rest rotation.
- `BubbleLayout`:
  - `Box Label(Pt tip, float w, float h, Box workArea)`: placed at tip + (10, 18) DIP and clamped;
  - `Box Message(Pt tip, float w, float h, Box workArea)`: placed at tip + (22, −6); it flips to the left side when it would cross the work area's right edge, then clamps.
- `enum CursorPresence { Following, Docked, Off }`.
- `CompanionStyle(string AccentHex, CursorPresence Presence, bool ShakeToDetach, bool ScreenHighlight, bool ReducedMotion)`. `static CompanionStyle From(AppSettings s, bool reducedMotion)`: `Paused` → Off; `CursorHidden` → Docked; otherwise Following. The accent is snapped.

- [ ] **Step 1: Write the failing tests.** `CursorMotionTests`:
  - `SpringSettlesWithin800ms`: from 0 to (100, 0) at 60 fps; |error| < 0.5 by 0.8 s.
  - `SpringOvershootUnder5Percent`: max X < 105.
  - `SpringStableWithHugeDt`: one `Step(…, 5.0)` stays finite and within (0, 110).
  - `FlightDurationClamped`: a 10 px flight is 0.3 s and a 5,000 px flight is 0.9 s.
  - `FlightStartsAndEndsOnEndpoints`.
  - `FlightArcsTowardScreenUp`: at the midpoint of a horizontal flight, Y < the line's Y.
  - `ShakeDetectedAfterSixReversals`: ±40 px zig-zags at 40 ms.
  - `SlowWaveIsNotShake`: the same path over 2 s.
  - `ShakeCooldownOneSecond`.
  - `TriangleTipAtAnchor`.
  - `MessageBubbleFlipsLeftAtRightEdge`.
  - `LabelBubbleOffset10Right18Below`.

  `CompanionStyleTests.FromSettings`: paused → Off, hidden → Docked, `#123456` → `#3380FF`.
- [ ] **Step 2: Run** `dotnet test tests/OpenClicky.Core.Tests --filter "FullyQualifiedName~CursorMotionTests|FullyQualifiedName~CompanionStyleTests"`. Expected: FAIL (types missing).
- [ ] **Step 3: Implement** the types above.
- [ ] **Step 4: Run** the same filter. Expected: PASS.
- [ ] **Step 5: Commit** `feat(companion): cursor spring, flight, shake and bubble layout`.

### Task C2: Layered overlay and companion cursor look

**Files:**
- Create: `apps/windows/OpenClicky.Overlay/LayeredWindow.cs` (an internal `NativeWindow` with the ex-styles above; `Present(Bitmap pargb, Point screenTopLeft)` calls `UpdateLayeredWindow(ULW_ALPHA)` with `AC_SRC_ALPHA`; `HideWindow()`).
- Create: `apps/windows/OpenClicky.Overlay/CursorRenderer.cs`.
- Create: `apps/windows/OpenClicky.Overlay/DpiScale.cs` (`float ForPoint(Point)` via `MonitorFromPoint` + `GetDpiForMonitor` / 96).
- Modify: rewrite `BuddyOverlayController.cs`. Keep `PartialStroke`, `ResponseBubblePlacement`, `SelectRegionAsync`, `TryGetBuddyVirtualPoint` and `IsPointFlyComplete`. Delete the mint face, the trail and `BuddyTipForm`.
- Modify: `IOverlayController.cs` (new default members), `TrayApplicationContext.Capture.cs` and `Shell/TrayApplicationContext.Shell.cs` (use `WindowHandles`), and `Shell/TrayApplicationContext.TalkFeedback.cs` (push the mic level).
- Modify: `TalkOrchestrator.cs`: in the `finally`, change `_overlay.Hide()` to `_overlay.ShowIdle()`.
- Create: `apps/windows/OpenClicky.App/Companion/TrayApplicationContext.Companion.cs`: `ApplyCompanionStyle()` runs at startup and after any `cursor.*` patch, `general.paused` or `OnSettingsApplied`.
- Modify: `OpenClicky.Overlay.Smoke/Program.cs` (assert the tip via `TryGetBuddyVirtualPoint` instead of `BuddyTipHandle`/`TrailPointCount`).

**Interfaces:**
- Consumes: C1 types.
- Produces (`IOverlayController`, each with a no-op default):
  - `void ShowIdle()`: the cursor is visible and following, with no bubble; the drawing is kept.
  - `void ApplyStyle(CompanionStyle style)`.
  - `void ShowMicLevel(double level)`.
  - `IReadOnlyList<nint> WindowHandles => []`: every overlay window that exists now.
- Semantics change:
  - `Hide()` now means "hide every overlay window until the next `Show*`/`ShowIdle`". It is used before a capture and keeps the drawing.
  - `ShowAnnotations` still works (it is mapped onto the drawing layer in C6).
  - `BuddyTipHandle` returns the cursor window handle.

**Behaviour (pinned):**
- **Visibility:** the cursor is always visible while Presence = Following, including when idle. It is hidden for Off and while `Hide()` is in force.
- **Cursor window:** the bitmap is sized to the content bounds each frame. A 16 ms timer runs while visible. The spring target is the real cursor + `TipOffset`.
- **Triangle:** rounded corners with radius `CornerFraction × side`. Fill: the accent. Rim: 1 DIP of the accent lerped 35 % toward white.
- **Glow:** 5 strokes of the triangle path at widths 4/8/12/16/20 DIP with alpha 40/28/18/10/5.
- **Listening:**
  - 5 bars, each 2 DIP wide with 2 DIP gaps and corner radius 1.5, at tip + (14, −4);
  - bar height = 3 + 9 × level × [0.55, 0.8, 1, 0.8, 0.55][i] DIP;
  - each frame a bar eases 35 % of the way to its target. The level is fed every 100 ms from the mic watch.
- **Thinking:** 3 dots of radius 2.3 orbiting the triangle centroid at distance 8.2. The orbit loops every 2.16 s. Each dot's alpha pulses on a 0.72 s beat, offset by i × 0.72 s.
- **Message bubble** (`ShowResponding`, `ShowTranscript`):
  - shows the current spoken line;
  - box: max width 300 DIP, Segoe UI Variable Text 11 DIP, line height 15, padding 10×7, radius 6;
  - colours: fill `#E61C1F26`, text `#F2F4F7`; at most 6 lines, then "…";
  - words reveal at 60 ms per word; the bubble holds 4 s after the last word (or until the next line), then fades over 400 ms.
- **Label bubble** (pointing): 11 DIP text, padding 8×4, radius 6, accent fill, white text, placed by `BubbleLayout.Label`.
- **Recordings:** `ApplyCaptureExclusion` and `OwnWindowsForCapture` iterate `_overlay.WindowHandles`.

- [ ] **Step 1:** Extend `CompanionWorkflowTests` with the fake overlay recording calls: `TurnEndsWithShowIdleNotHide`, which asserts that the last overlay call of a completed text turn is `ShowIdle`. Run `dotnet test tests/OpenClicky.Core.Tests --filter FullyQualifiedName~CompanionWorkflowTests`. Expected: FAIL.
- [ ] **Step 2:** Implement the controller, renderer, layered window, DPI helper and wiring above.
- [ ] **Step 3:** Run the Core tests (PASS), then `dotnet build OpenClicky.sln -c Release` (0 warnings), then `dotnet run --project apps/windows/OpenClicky.Overlay.Smoke`. Expected: the smoke prints its PASS line, with the tip within 8 px of the POINT target.
- [ ] **Step 4: Commit** `feat(companion): glowing triangle cursor with listening, thinking and bubbles`.

### Task C3: Dock, hide, shake to detach

**Files:**
- Create: `apps/windows/OpenClicky.Overlay/ClickCatcher.cs`: a tiny topmost, non-activating window with alpha 1/255 so it receives clicks. It has `Show(Rectangle)`, `Hide()` and `event Action Clicked`. C7 reuses it.
- Modify: `BuddyOverlayController.cs` (dock and detach states).
- Modify: `Companion/TrayApplicationContext.Companion.cs` (the notch point, `DetachedClicked` → `ToggleConversation()`, and `CompanionDockForm` hidden while `_shell is { IsActive: true }`, shown again on classic fallback).
- Modify: `IOverlayController.cs`: add `void SetNotchPoint(int virtualX, int virtualY) { }`.

**Interfaces:**
- Consumes: `CompanionStyle.Presence`, `ShakeDetector`, `FlightPath`.
- Produces: `BuddyOverlayController.DetachedClicked` (an event on the concrete class).

**Behaviour (pinned):**
- **Notch point:** the top centre of the monitor that hosts the shell surface: (monitor left + width / 2, monitor top + 4). It is updated when the surface moves monitor.
- **Docking:**
  - Following → Docked: the bubble "i'll wait up here" holds 1.1 s, the cursor flies to the notch point, then it scales 1 → 0 and fades over 0.6 s.
  - Docked → Following: the cursor appears at the notch point at scale 0, grows to 1 over 0.3 s, and springs to the cursor.
- **While docked:**
  - listening and thinking show only in the notch phase (A);
  - `PointAt` and drawing fly the cursor out from the notch;
  - `ShowIdle` flies it back and docks again.
- **Shake to detach** (`ShakeToDetach` on, Presence = Following, state idle or listening; detected from cursor samples on the 16 ms tick):
  - the companion stops following and stays where it is;
  - a 36×36 DIP `ClickCatcher` covers it;
  - a click raises `DetachedClicked` and reattaches;
  - with no click, it reattaches after 3 s by springing back to the cursor.

- [ ] **Step 1:** Add the pure `DetachState` to `Companion/CursorMotion.cs`:
  - `void Shake(Pt at, long ms)`: ignored unless Presence = Following;
  - `bool Detached`, `Pt At`;
  - `bool Tick(long ms)`: true when it reattaches, 3,000 ms after the shake;
  - `void Clicked()`: reattaches.

  Tests in `CursorMotionTests`: `DetachReattachesAfterThreeSeconds`, `ClickReattaches`, `ShakeIgnoredWhenDocked`. Run with `--filter FullyQualifiedName~CursorMotionTests`. Expected: FAIL.
- [ ] **Step 2:** Implement `DetachState`, the dock/detach rendering, `ClickCatcher` and the tray wiring.
- [ ] **Step 3:** Run the Core tests and the build: PASS, 0 warnings.
- [ ] **Step 4: Commit** `feat(companion): dock into the notch, hide, shake to detach`.

### Task C4: Pointing and click ring

**Files:**
- Create: `apps/windows/OpenClicky.Core/Companion/ClickRingLayout.cs`.
- Modify: `BuddyOverlayController.cs`: `PointAt` flies by `FlightPath`, the ring is drawn on the drawing window, and the cursor returns on `ShowIdle`.
- Modify: `IOverlayController.cs`: add the overload `void PointAt(int virtualX, int virtualY, string? label, string? spokenText, Box? controlFrame) => PointAt(virtualX, virtualY, label, spokenText)`.
- Test: `tests/OpenClicky.Core.Tests/ClickRingLayoutTests.cs`.

**Interfaces (Produces):**
- `ClickRingLayout.From(Box? controlFrame, Pt centre, float dipScale, Box workArea) -> (Box Ring, float CornerRadius, Pt LabelAnchor, bool LabelBelow)`.
  - With a frame: the frame inflated by 6 DIP, corner radius min(10, h / 2).
  - Without a frame: a circle of radius 28 DIP.
  - The ring's half-extent is clamped to 28–180 DIP.
  - The label sits 8 DIP above the ring's top centre, or below the ring when there is no room above.

**Behaviour (pinned):**
- **Flight:** the cursor's tip lands exactly on the target.
- **Ring:**
  - a dashed rounded outline, 2.5 DIP wide with dashes of 6/4, in the accent;
  - a glow of 3 strokes at alpha 30/18/8 and a fill of the accent at 8 % alpha;
  - a breathing pulse: alpha 0.75↔1 and inflate 0↔3 DIP on a 1.6 s cycle;
  - a spring reveal from scale 0.85 to 1 (k = 300, c = 26).
- **Return:** a return flight to the following position starts on `ShowIdle`. If the real cursor moves more than `ReturnCancelDistance` DIP from where it was when the return began, the flight is cancelled and the spring follows the cursor.
- **Agent clicks:** the `AgentOrchestrator` click `PointAt` keeps working unchanged.

- [ ] **Step 1:** `ClickRingLayoutTests`:
  - `NoFrameIsCircle28`;
  - `FrameInflatedBySix`;
  - `HugeFrameClampedTo180`;
  - `TinyFrameAtLeast28`;
  - `LabelFlipsBelowAtTopEdge`.

  Run with `--filter FullyQualifiedName~ClickRingLayoutTests`. Expected: FAIL.
- [ ] **Step 2:** Implement the layout and the ring renderer.
- [ ] **Step 3:** Run the tests (PASS), the build, and the Overlay smoke (PASS).
- [ ] **Step 4: Commit** `feat(companion): dashed glowing click ring and spring flight`.

### Task C5: Drawing commands and drawing document (Core)

**Files:**
- Create in `apps/windows/OpenClicky.Core/Drawing/`: `DrawOp.cs`, `DrawCommandParser.cs`, `DrawingMapper.cs`, `DrawingDocument.cs`, `DrawingCueTracker.cs`.
- Modify: `StreamingReplySpeech.cs`: `Clean` also strips `[DRAW:` tags, including partial ones, and add `internal static int CountSentenceEnds(string clean)` using the same `.`/`!`/`?`-then-whitespace-or-end rule.
- Modify: `GuidedWalkthrough.cs`: `VisualTags` also matches `DRAW`.
- Test: `tests/OpenClicky.Core.Tests/DrawCommandParserTests.cs`, `DrawingMapperTests.cs`, `DrawingDocumentTests.cs`, `DrawingCueTrackerTests.cs`.

**Interfaces (Produces):**
- `enum DrawOpKind { Line, Arrow, Curve, Poly, Path, Circle, Rect, Dot, Text, Highlight, Graph, Timeline, Point, Target, Frame, Ring, Trace, Label, Underline, Strike, Cross, Check, Star, Scroll, Spotlight, Badge, Button, Card, Sticky, Tie, Figure, Recolor, Move, MoveTo, Scale, Erase, Undo, Clear }`.
- `DrawOp(DrawOpKind Kind, string? Id, IReadOnlyList<Pt> Points, Box Box, string? Text, string? Color, int? Screen, IReadOnlyDictionary<string,string> Args, int Offset, FigureSpec? Figure = null)`. `Offset` is the tag's start index in the raw reply. `FigureSpec` is declared in C8; for now create it as an empty placeholder record in `Drawing/FigureSpec.cs`, which C8 fills.
- `DrawCommandParser.Parse(string reply, bool partial = false) -> (string Text, IReadOnlyList<DrawOp> Ops)` and `DrawCommandParser.FromMarks(IReadOnlyList<ScreenAnnotation> marks) -> IReadOnlyList<DrawOp>`.
  - Constants: `MaxTags = 24`, `MaxPoints = 32`, `MaxPathCommands = 64`, `MaxLabel = 60`, `MaxText = 280`, `MaxButtons = 3`, `MaxPrompt = 300`.
- `DrawingMapper.ToVirtual(IReadOnlyList<DrawOp> ops, IReadOnlyList<ScreenCaptureResult> screens) -> IReadOnlyList<(DrawOp Virtual, DrawOp Source, float Scale)>`.
  - Screen selection uses `ScreenAnnotations.Map` rules.
  - An op is dropped if any point or box corner lies outside its screenshot.
  - Edit ops pass through unmapped.
- `DrawPart(string Id, DrawOp Op, DrawOp Source, float Scale, int Seq)`: `Op` is in virtual pixels and `Source` in screenshot pixels.
- `PartAnimation(DrawPart Part, TimeSpan Start, TimeSpan Duration, bool Erase)`.
- `DrawingDocument`:
  - constants `MaxParts = 64`, `MaxUndo = 20`;
  - `IReadOnlyList<DrawPart> Parts`, `bool IsEmpty`;
  - `IReadOnlyList<PartAnimation> Apply(IReadOnlyList<(DrawOp Virtual, DrawOp Source, float Scale)> ops, TimeSpan lineDuration)`;
  - `string Describe()` (≤ 2,000 chars, screenshot coordinates);
  - `void Clear()`.
- `DrawingCueTracker`: `void Update(string rawPartial)`, `IReadOnlyList<DrawOp> TakeThrough(int sentenceIndex)`, `IReadOnlyList<DrawOp> TakeAll()`.
- `DrawingPacing.LineDuration(string text, double speed) => max(0.6 s, text.Length × 0.065 s / speed)`.

**Grammar (pinned):**
- Tag form: `[DRAW:<op> key=value …]`, with lowercase keys.
- Value forms: integers; `pts=x1,y1,x2,y2,…`; `#RRGGBB`; double-quoted strings using `\"` and `\]` escapes.
- `id` is 1–24 characters of `[a-z0-9_-]`. A missing id becomes `p{seq}`.
- Common keys: `id`, `color`, `label`, `screen`.
- Unknown keys are ignored. An unknown op, a missing required key, a non-integer, a value ≥ 100,000, or a 25th tag removes the tag and produces no op.

| op | required keys | optional keys |
|---|---|---|
| `line`, `arrow` | `pts` (≥ 2) | `style` = `pen`, `marker` or `highlighter` |
| `curve` | `pts` (exactly 4) | |
| `poly` | `pts` (≥ 3) | `closed=1` |
| `path` | `d` (absolute M/L/C/Q/Z) | |
| `circle` | `x y r`, or `x y w h` | |
| `rect` | `x y w h` | `r` (corner radius) |
| `dot` | `x y` | |
| `text` | `x y text` | `size` (10–48) |
| `highlight` | `x y w h` | |
| `graph` | `x y w h series` (`1,4,9;2,3,5`: ≤ 4 series × 24 points) | `xlabel`, `ylabel` |
| `timeline` | `x y w items` (`"1990:Web;2007:Phones"`, ≤ 8 items) | |
| `point` | `x y label` | |
| `target` | `x y w h label` | `hover=1` |
| `frame`, `spotlight` | `x y w h` | |
| `ring` | `x y r` | |
| `trace` | `pts` (≥ 3) | |
| `label` | `x y text` | |
| `underline`, `strike` | `x y w` | |
| `cross`, `check`, `star` | `x y` | `size` (default 24) |
| `scroll` | `x y dir` (`up` or `down`) | |
| `badge` | `x y n` (1–15) | |
| `button` | `x y text prompt` | |
| `card` | `x y title text` | `w` (200–360), `stock` |
| `sticky` | `x y text` | `stock` |
| `tie` | `pts` (exactly 2) | |
| edits | `recolor id color` · `move id dx dy` · `moveto id x y` · `scale id factor` (0.25–4) · `erase id` | `undo` and `clear` take no keys |

**Document rules (pinned):**
- Adding a part with an existing id replaces that part. A 65th part is dropped.
- New parts reveal one after another: each takes `clamp(lineDuration / n, 0.25 s, 1.2 s)` and starts at i × that duration.
- `recolor`, `move`, `moveto` and `scale` apply instantly. `dx`/`dy` are converted to virtual pixels with the part's `Scale`.
- `erase` removes the part and returns an `Erase` animation of 0.4 s.
- `undo` restores the previous parts snapshot (from a stack of 20).
- `clear` empties the document.
- `Describe()` writes one line per part: `id kind "label" at x,y w×h screenN`, with labels JSON-quoted.
- **Cue rule:** an op's sentence index is `CountSentenceEnds(StreamingReplySpeech.Clean(raw[..op.Offset]))`.

- [ ] **Step 1: Write the failing tests.**
  - `DrawCommandParserTests`:
    - `ParsesEveryOp`: one tag per row of the grammar table.
    - `QuotedTextEscapes`.
    - `RejectsMalformedAndOutOfRange`: unknown op; missing `r`; `x=-5`; `x=999999`; a 33-point `pts`; a 25th tag. Each tag is stripped and none produces an op.
    - `StripsDanglingTagWhenPartial`.
    - `FromMarksMapsAllSevenShapes`.
  - `DrawingMapperTests`:
    - `MapsScreenTwo`;
    - `DropsOutOfBounds`;
    - `ScalesRadiusByMonitorRatio`;
    - `EditsPassThrough`.
  - `DrawingDocumentTests`:
    - `SequentialRevealTiming`: 4 parts in 2 s → each 0.5 s, starting at 0/0.5/1/1.5 s.
    - `RevealClampedTo250msAnd1200ms`.
    - `SameIdReplaces`.
    - `MaxParts64`.
    - `MoveUsesPartScale`.
    - `EraseReturnsReverseAnimation`.
    - `UndoRestoresPrevious`.
    - `DescribeUsesScreenshotCoordinatesAndQuotesLabels`.
    - `DescribeCappedAt2000`.
  - `DrawingCueTrackerTests`:
    - `TagAfterFirstSentenceCuesLineOne`;
    - `TagsTakenOnce`;
    - `PartialTagNeverEmitted`;
    - `TakeAllReturnsRemaining`.
  - `CompanionWorkflowTests.DrawTagsNeverSpoken` (via `StreamingReplySpeech.Clean`).
- [ ] **Step 2: Run** `dotnet test tests/OpenClicky.Core.Tests --filter "FullyQualifiedName~Draw"`. Expected: FAIL.
- [ ] **Step 3: Implement** the types above.
- [ ] **Step 4: Run** the same filter. Expected: PASS. Then run the full Core suite: PASS (the existing `GuidedWalkthroughTests` and `StreamingReplySpeech` users stay green).
- [ ] **Step 5: Commit** `feat(drawing): DRAW tag parser, drawing document and speech cues`.

### Task C6: Drawing renderer, ink style, speech pacing

**Files:**
- Create: `apps/windows/OpenClicky.Core/Drawing/InkStyle.cs`, `ShapeGeometry.cs`, `LabelLayout.cs`.
- Create: `apps/windows/OpenClicky.Overlay/DrawingWindow.cs`, `DrawingRenderer.cs`. `DrawingRenderer` is public static, so C11's replay and print can reuse it.
- Modify: `IOverlayController.cs`: add `void ShowDrawing(IReadOnlyList<DrawPart> parts, IReadOnlyList<PartAnimation> animations) { }`, `void FinishDrawing() { }`, `bool HasDrawing => false`.
- Modify: `TalkOrchestrator.cs`, `CompanionPrompts.cs`, `TrayApplicationContext.cs` (the Esc handler) and `Companion/TrayApplicationContext.Companion.cs` (the stale watch).
- Test: `tests/OpenClicky.Core.Tests/InkStyleTests.cs`, `ShapeGeometryTests.cs`, `LabelLayoutTests.cs`, `CompanionPromptsTests.cs`, plus additions to `CompanionWorkflowTests.cs`.

**Interfaces:**
- Consumes: C5 (`DrawingDocument`, `DrawingCueTracker`, `DrawingMapper`, `DrawingPacing`), C2 (`ShowIdle`) and C4 (`ClickRingLayout`, used for `point` and `target`).
- Produces:
  - `InkStyle.Wobble(IReadOnlyList<Pt> polyline, int seed, float amplitude = 1.2f, float wavelength = 28f) -> IReadOnlyList<Pt>`: resample every 4 px, offset along the normal; deterministic per seed.
  - `InkStyle.PenWidth(float t, float baseWidth = 3.2f)`: tapers to 40 % over the first and last 12 % of the length.
  - Constants: `HighlighterWidth = 14`, `HighlighterAlpha = 0.35f`, `WashAlpha = 0.18f`.
  - `ShapeGeometry.Outline(DrawOp op) -> IReadOnlyList<IReadOnlyList<Pt>>`:
    - circle → a 64-point ellipse;
    - rect → a rounded polyline;
    - arrow → a shaft plus a 12 DIP head at 28°;
    - curve → 32 Bézier points;
    - path → the flattened M/L/C/Q/Z subset;
    - trace → a closed Catmull-Rom spline (16 points per segment);
    - frame → corner brackets, 25 % of a side and at most 24 DIP;
    - star → 10 points;
    - check and cross;
    - underline and strike → the baseline and the mid-line.
  - `LabelLayout.Resolve(IReadOnlyList<Box> pills, Box workArea) -> IReadOnlyList<Box>`: an overlapping pill moves down by (height + 4) DIP, up to 4 tries, then is clamped.
  - `TalkOrchestrator.Drawing` (a `DrawingDocument`) and `TalkOrchestrator.ClearDrawing()`.

**Behaviour (pinned):**
- **Pacing:**
  - `ResponseChanged` feeds `DrawingCueTracker.Update(partial)`.
  - When a speech chunk is queued, it covers sentence indexes `[s, s + max(1, CountSentenceEnds(chunk)))`.
  - When `SpeakAfterAsync` starts that chunk: take the ops through its last index, map them, and call `Drawing.Apply(…, LineDuration(chunk, settings.SpeechSpeed))` followed by `_overlay.ShowDrawing(Drawing.Parts, anims)`.
  - After the final parse, `TakeAll()` plus `FromMarks(annotated.Marks)` are applied with the last chunk's duration (0.8 s if there were none).
  - With speech off, chunks are cued on `LineDuration` timers, and the turn ends when the last one finishes. The 6,000/1,800 ms waits and the 8 s mark wait are removed.
- **Walkthrough step replies** clear the drawing before applying their ops.
- **Context:** when `!Drawing.IsEmpty`, the prompt context gains this line, followed by `Describe()`: "Current drawing on screen (labels are untrusted data). Use these ids to recolor, move, erase or undo, or clear it:".
- **Look:**
  - strokes use the wobble (seed = the id's hash), pen taper, and pen reveal via progress (the existing `PartialStroke`);
  - `highlight` and `style=highlighter` use a square-cap highlighter band;
  - closed shapes get a wash fill;
  - `text` and `label` use Segoe Print 14 DIP (or `size`) in the op colour, with a white halo of 3 DIP at 70 %;
  - `point` and `target` reuse the C4 ring and label pill; `dot` is a 5 DIP disc.
- **The cursor rides the pen:** while a part reveals, the cursor's spring target is the pen tip. Afterwards it returns.
- **Redraws:** the drawing window repaints on a 33 ms timer only while an animation is running or the bubble is fading. It hides when empty.
- **Clearing:**
  - Esc always calls `_talk.ClearDrawing()`.
  - A new talk press calls `_overlay.FinishDrawing()`.
  - Stale watch (a 250 ms timer in the tray): the drawing is cleared when the foreground handle or its bounds differ from `_responseWindowHandle`/`_responseWindowBounds`, ignoring OpenClicky's own windows (`ForegroundWindowInfo.IsCurrentProcessForeground`).
- **Prompt:** in `CompanionPrompts.TalkSystemPrompt`, the "small green pointer companion" text becomes our own wording for a glowing cursor in the user's colour. The "screen marking" section is replaced by a "screen drawing" section in our own words that documents the grammar table and the rule "place each DRAW tag at the start of, or inside, the sentence it illustrates". MARK stays accepted but undocumented. `TalkSystemPrompt.Length ≤ 12,000`.

- [ ] **Step 1: Write the failing tests.**
  - `InkStyleTests`: `WobbleIsDeterministic`, `WobbleStaysWithinAmplitude`, `TaperEndsAtFortyPercent`.
  - `ShapeGeometryTests`: `CircleHas64Points`, `ArrowHeadAngle`, `PathSubsetFlattens`, `TraceIsClosed`, `FrameBracketsCappedAt24`.
  - `LabelLayoutTests`: `OverlappingPillsSeparate`.
  - `CompanionPromptsTests.TalkPromptStaysWithinBudget` (≤ 12,000 characters, contains "[DRAW:").
  - `CompanionWorkflowTests`:
    - `DrawOpsCuedWithTheirSentence`: a fake TTS records the order; the second sentence's tag is shown at the second `SpeakAsync`.
    - `DrawingPersistsAfterTurnAndIsDescribedNextTurn`.
    - `ClearDrawingEmptiesOverlay`.
- [ ] **Step 2: Run** `dotnet test tests/OpenClicky.Core.Tests --filter "FullyQualifiedName~InkStyle|FullyQualifiedName~ShapeGeometry|FullyQualifiedName~LabelLayout|FullyQualifiedName~CompanionPrompts|FullyQualifiedName~CompanionWorkflow"`. Expected: FAIL.
- [ ] **Step 3: Implement** the geometry, renderer, drawing window, orchestrator pacing, prompt, Esc handling and stale watch.
- [ ] **Step 4: Run** the full Core suite and the build. Expected: PASS, 0 warnings.
- [ ] **Step 5: Commit** `feat(drawing): hand-drawn renderer paced to speech with live edits`.

### Task C7: Screen helpers and drawing buttons

**Files:**
- Create: `apps/windows/OpenClicky.Core/Drawing/HelperGeometry.cs`.
- Modify: `DrawingRenderer.cs`, `BuddyOverlayController.cs` (button click catchers) and `Companion/TrayApplicationContext.Companion.cs`.
- Test: `tests/OpenClicky.Core.Tests/HelperGeometryTests.cs`.

**Interfaces:**
- Consumes: C5 ops; C3's `ClickCatcher`.
- Produces:
  - `HelperGeometry.Graph(DrawOp op) -> (IReadOnlyList<Pt> Axes, IReadOnlyList<IReadOnlyList<Pt>> Series, IReadOnlyList<(Pt At, string Text)> Ticks)`: series are scaled into the box with a 10 % margin; there are 5 ticks per axis; all-equal values give a flat line at mid-height.
  - `HelperGeometry.Timeline(DrawOp op) -> (IReadOnlyList<Pt> Axis, IReadOnlyList<(Pt Tick, string Label)> Items)`: evenly spaced.
  - `HelperGeometry.Scroll(Pt at, bool down) -> IReadOnlyList<IReadOnlyList<Pt>>`: 3 chevrons of 16 DIP, 10 DIP apart.
  - `HelperGeometry.Tie(Pt from, Pt to) -> IReadOnlyList<Pt>`: a quadratic arc bulging 20 % of its length.
  - `BuddyOverlayController.ButtonClicked` (`event Action<string label, string prompt>`).

**Behaviour (pinned):**
- `spotlight`: everything outside the box is dimmed with black at 55 % alpha; the box keeps 8 DIP rounded corners.
- `badge`: a 22 DIP accent disc with a white Segoe UI Variable Display semibold 12 numeral.
- **Paper stocks** (our own palette): `cream #FBF6EA` (default card), `butter #FFF1B8` (default sticky), `sky #DCEBFF`, `mint #DCF4E6`, `blush #FBE0E3`, `kraft #E6D2AE`. Ink is `#1F2430`.
- `card`:
  - width `w` (default 280);
  - title in Segoe UI Variable Display semibold 14, body in Segoe Print 12, up to 8 lines;
  - a deterministic tilt of ±2° by id;
  - a 40×12 DIP tape strip on top (white, 60 %), and a soft shadow of 3 offset fills.
- `sticky`: 160×160 DIP with the same text style.
- `button`:
  - drawn as an accent pill;
  - a `ClickCatcher` sits over it, at most 3 per drawing;
  - a click raises `ButtonClicked`, and the tray submits the prompt with `_talk.SubmitTextAsync(prompt)`, showing the label as the user's row;
  - the reply passes through the normal talk pipeline, so `[OPEN]`/`[TYPE]` still follow `AgentAccessPolicy`;
  - the catchers are removed with their parts.

- [ ] **Step 1:** `HelperGeometryTests`:
  - `GraphFitsBoxWithMargin`;
  - `GraphFlatSeriesMidHeight`;
  - `TimelineEvenlySpaced`;
  - `ScrollThreeChevrons`;
  - `TieBulges`.

  Run with `--filter FullyQualifiedName~HelperGeometryTests`. Expected: FAIL.
- [ ] **Step 2:** Implement the geometry, rendering and button wiring.
- [ ] **Step 3:** Run the Core tests and the build. Expected: PASS.
- [ ] **Step 4: Commit** `feat(drawing): spotlight, cards, stickies, graphs, timelines and buttons`.

### Task C8: 3D figures

**Files:**
- Create in `apps/windows/OpenClicky.Core/Drawing/`: `FigureSpec.cs` (replacing the C5 placeholder), `FigureBlockParser.cs`, `FigureMesh.cs`, `FigureCamera.cs`.
- Create: `apps/windows/OpenClicky.Overlay/FigureWindow.cs` (WPF).
- Modify: `OpenClicky.Overlay.csproj` (add `<UseWPF>true</UseWPF>`), `DrawingRenderer`/`BuddyOverlayController` (create and close a `FigureWindow` per figure part), `TalkOrchestrator.cs` (parse the block before `ScreenAnnotations`; figures become `DrawOpKind.Figure` ops on the last line), `StreamingReplySpeech.Clean` (strip ```` ```figure ```` blocks), and `CompanionPrompts.cs` (≤ 800 characters of figure documentation in our own words).
- Test: `tests/OpenClicky.Core.Tests/FigureMeshTests.cs`, `FigureBlockParserTests.cs`, `FigureCameraTests.cs`.

**Interfaces (Produces):**
- `enum FigureKind { Box, Sphere, Cylinder, Cone, Torus, Capsule, Tube, Lathe, Extrude, Gear, Spring, Arrow }`.
- `FigurePart(FigureKind Kind, Vector3 Size, Vector3 At, Vector3 RotateDegrees, string Color, string? Label, IReadOnlyDictionary<string,double> Params, IReadOnlyList<Vector2>? Profile)`, using `System.Numerics`.
- `FigureSpec(string Id, Box Box, int? Screen, bool Spin, string? Title, IReadOnlyList<FigurePart> Parts)`.
- `FigureBlockParser.Parse(string reply) -> (string Text, IReadOnlyList<FigureSpec> Figures)`. It reads a fenced ```` ```figure ```` JSON block:
  ```json
  {"id":"f1","box":[x,y,w,h],"screen":1,"spin":true,"title":"…","parts":[{"kind":"gear","size":[1,0.2,1],"at":[0,0,0],"rotate":[0,0,0],"color":"#3380FF","label":"drive gear","teeth":12}]}
  ```
  - Limits: at most 2 figures per reply and 12 parts per figure; up to 24 profile/path points; at most 20,000 triangles per figure, or the figure is dropped.
  - Unknown kinds are dropped. Malformed JSON keeps the text and strips the block.
- `Mesh(IReadOnlyList<Vector3> Positions, IReadOnlyList<Vector3> Normals, IReadOnlyList<int> Indices)` and `FigureMesh.Build(FigurePart part) -> Mesh`.
  - Segment counts: sphere 32×16; cylinder and cone 32; torus 32×16; capsule 32 around × 8 per cap; lathe 32 around; tube 12 around; gear with `teeth` 6–40 (default 12); spring with `coils` 1–12 (default 5), 24 segments per coil × 8 around; arrow = cylinder shaft + cone head.
- `FigureCamera`:
  - `Fit(Box3 bounds) -> CameraSpec`: perspective, FOV 35°, looking from direction (1, 0.8, 1.4) normalised at distance r / sin(17.5°) × 1.1;
  - `Project(Vector3 world, CameraSpec cam, float width, float height) -> Pt?`: returns null when the point is behind the camera.

**Behaviour (pinned):**
- **Window:** a `FigureWindow` (WPF `Window`: `WindowStyle=None`, `AllowsTransparency=true`, transparent background, `Topmost`, `ShowActivated=false`, `ShowInTaskbar=false`). The ex-styles are set in `SourceInitialized` through `WindowInteropHelper`. It is positioned with `SetWindowPos` in physical pixels at the mapped box.
- **Scene:** a `Viewport3D` with `AmbientLight #505050` and a `DirectionalLight` (white, direction (−1, −1.5, −2)). Materials: `DiffuseMaterial(color)` + `SpecularMaterial(white, 24)`.
- **Labels:** accent pills of 11 DIP in a `Canvas`, placed at `FigureCamera.Project` of each part's centre and re-projected on `CompositionTarget.Rendering` while spinning.
- **Motion:** the reveal scales 0 → 1 over 0.5 s with ease-out back. `spin` rotates about Y at 20°/s. Reduced motion means no scale-in and no spin.
- **Lifetime:** figures clear with the drawing.
- **Handles:** `WindowHandles` includes the figure windows. The existing `scene3d` artifact is untouched.

- [ ] **Step 1: Write the failing tests.**
  - `FigureMeshTests` (each check runs per kind):
    - `IndicesInRangeAndTriangles`;
    - `ClosedSolidsWatertight`: every edge is shared by exactly 2 triangles, for box, sphere, cylinder, cone, torus and capsule;
    - `NormalsUnitLength`;
    - `BoundsMatchSizeWithin1Percent`;
    - `GearHasTeeth` (the radial maxima count equals `teeth`).
  - `FigureBlockParserTests`: `ParsesBlockAndStripsIt`, `TooManyPartsDropped`, `UnknownKindDropped`, `MalformedJsonKeepsText`, `TriangleBudgetEnforced`.
  - `FigureCameraTests`: `CentreProjectsToViewportCentre`, `BehindCameraIsNull`.
- [ ] **Step 2: Run** `dotnet test tests/OpenClicky.Core.Tests --filter "FullyQualifiedName~Figure"`. Expected: FAIL.
- [ ] **Step 3: Implement** the parser, meshes, camera, `FigureWindow` and wiring.
- [ ] **Step 4: Run** the full Core suite and the build. Expected: PASS, 0 warnings.
- [ ] **Step 5: Commit** `feat(drawing): 3D figures in WPF Viewport3D`.

### Task C9: Screen highlight gesture

**Files:**
- Create: `apps/windows/OpenClicky.Core/Companion/Highlight.cs` (`StrokeKind`, `HighlightStroke`, `HighlightGesture`, `StrokeSegmenter`, `StrokeClassifier`, `HighlightDescriber`).
- Create: `apps/windows/OpenClicky.Agent/UiaTextHarvester.cs`, `apps/windows/OpenClicky.App/Companion/HighlightCompositor.cs`.
- Modify: `CoordinateMapper.cs` (add the reverse mapping), `IOverlayController.cs` (add `void AddHighlightPoint(int x, int y) { }` and `void ClearHighlight() { }`), `TalkOrchestrator.cs` (new optional constructor parameters) and `Companion/TrayApplicationContext.Companion.cs` (the recorder).
- Test: `tests/OpenClicky.Core.Tests/HighlightTests.cs`, plus additions to `CoordinateMapperTests.cs` and `CompanionWorkflowTests.cs`.

**Interfaces (Produces):**
- `enum StrokeKind { Circled, LineFromTo, LineAcross, Scribbled, Path }`.
- `HighlightStroke(StrokeKind Kind, IReadOnlyList<Pt> Points, Box Bounds, string TextUnder)`.
- `HighlightGesture(IReadOnlyList<HighlightStroke> Strokes, IReadOnlyList<Pt> Dwells, DateTimeOffset EndedAt)`.
- `StrokeSegmenter`: `void Add(Pt p, long ms)`, `IReadOnlyList<IReadOnlyList<Pt>> Finish()`, `IReadOnlyList<Pt> Dwells`. A dwell is ≥ 350 ms within 12 px; it ends the current stroke and is recorded once per rest.
- `StrokeClassifier.Classify(IReadOnlyList<Pt> points) -> StrokeKind?`. Let L = the path length, D = the bounding-box diagonal, G = |end − start|, R = direction reversals (segments ≥ 6 px), T = total turning. Then:
  - null when L < 24;
  - **Circled** when G ≤ 0.35 D, L ≥ 1.8 D and T ≥ 270°;
  - **Scribbled** when R ≥ 6 and L ≥ 3 D;
  - when G / L ≥ 0.85: **LineAcross** if within 20° of horizontal, else **LineFromTo**;
  - otherwise **Path**.
- `HighlightDescriber.Describe(HighlightGesture g, IReadOnlyList<ScreenCaptureResult> screens) -> string` (≤ 2,400 chars):
  - one numbered line per stroke: the kind in plain words, the box in screenshot pixels, `screenN`, and the region on a 3×3 grid ("upper left" … "centre" … "lower right");
  - the UIA text as a JSON-quoted untrusted string;
  - dwells as "rested on x,y";
  - an opening sentence saying the marks are composited into the image in the user's colour.
- `CoordinateMapper.ToScreenshot(int virtualX, int virtualY, IReadOnlyList<ScreenCaptureResult> screens) -> (int Screen, int X, int Y)?`.
- `UiaTextHarvester.Harvest(IReadOnlyList<Pt> samples, int ownProcessId, TimeSpan budget) -> string`. It samples up to 24 points along the stroke (plus the centre for Circled) with `AutomationElement.FromPoint`, and collects distinct `Name` values and `ValuePattern.Value` (never when `IsPassword`). It skips elements of our own process, stops at 500 chars, and the budget is 400 ms in total.
- `HighlightCompositor.Composite(IReadOnlyList<ScreenCaptureResult> screens, HighlightGesture g, string accentHex) -> IReadOnlyList<ScreenCaptureResult>`. It decodes the JPEG, draws the strokes in the accent at 55 % alpha with width 8 × (pixel/monitor ratio) and dwells as rings of 10 px, then re-encodes with `CaptureImageEncoder.BitmapToJpeg`.
- New `TalkOrchestrator` constructor parameters: `Func<HighlightGesture?>? takeHighlight = null` and `Func<IReadOnlyList<ScreenCaptureResult>, HighlightGesture, IReadOnlyList<ScreenCaptureResult>>? compositeHighlight = null`. After an all-monitor or walkthrough capture, a gesture taken within 10 s is composited and `Describe()` is appended to the context. The gesture is consumed once.

**Behaviour (pinned):**
- **Recording:** the recorder runs from `HeldTalkStarted` until the talk release, and only when `ScreenHighlight` is on and the companion presence is not Off. It samples `GetCursorPos` every 16 ms.
- **Trail:** the overlay draws the trail on the drawing window at 8 DIP and 55 % alpha in the accent. Each sample fades over 1 s after it was drawn.
- **On release:** segment, classify (null strokes are dropped), harvest the UIA text, and store the gesture. A hold without movement yields no gesture.
- **Esc** clears the trail.

- [ ] **Step 1: Write the failing tests.**
  - `HighlightTests`:
    - `CircleIsCircled` (a generated r = 60 circle);
    - `HorizontalLineIsAcross`;
    - `DiagonalLineIsFromTo`;
    - `ZigZagIsScribbled`;
    - `OpenArcIsPath`;
    - `TinyJitterIgnored` (a 10 px wiggle → null);
    - `DwellSplitsStrokesAndIsRecordedOnce`;
    - `DescribeNamesRegionAndQuotesText`;
    - `DescribeCapped`.
  - `CoordinateMapperTests.ToScreenshotRoundTrips`.
  - `CompanionWorkflowTests`:
    - `HighlightCompositedAndDescribedOnce` (fakes: the second turn gets no gesture);
    - `RegionAskIgnoresHighlight`.
- [ ] **Step 2: Run** `dotnet test tests/OpenClicky.Core.Tests --filter "FullyQualifiedName~Highlight|FullyQualifiedName~CoordinateMapper"`. Expected: FAIL.
- [ ] **Step 3: Implement** the Core types, the harvester, the compositor and the recorder wiring.
- [ ] **Step 4: Run** the full Core suite and the build. Expected: PASS.
- [ ] **Step 5: Commit** `feat(companion): draw on screen while talking, with strokes and text sent along`.

### Task C10: Walkthrough targets and click-to-advance

**Files:**
- Create: `apps/windows/OpenClicky.Core/Companion/WalkthroughTargets.cs` (`GuidedTarget`, `TargetEvent`, `WalkthroughTargetWatch`, `SnapCandidate`, `TargetSnap`, `GuidedTargets`).
- Create: `apps/windows/OpenClicky.Core/Companion/CompanionCards.cs`, which C11 and C12 extend.
- Create: `apps/windows/OpenClicky.Agent/UiaControlSnapper.cs`.
- Modify: `GuidedWalkthrough.cs` (step cap, hover marker, end reason), `TalkOrchestrator.cs` (a snap function for every POINT; the end-reason context), `Companion/TrayApplicationContext.Companion.cs` (the click and hover watch, plus the card in shell mode) and `TrayApplicationContext.cs` (`WalkthroughChanged`: in shell mode a card replaces `WalkthroughForm`; classic mode keeps the form).
- Test: `tests/OpenClicky.Core.Tests/WalkthroughTargetsTests.cs`, plus additions to `GuidedWalkthroughTests.cs`.

**Interfaces (Produces):**
- `GuidedTarget(Box Frame, Pt Centre, float Radius, bool IsHover, string Label)`.
- `enum TargetEvent { None, Advance, LeftGuide, Expired, GuideTimedOut }`.
- `WalkthroughTargetWatch`:
  - constants `ArmMs = 120_000`, `GuideIdleMs = 600_000`, `HoverMs = 2_000`, `HitSlop = 6`;
  - `void Arm(GuidedTarget t, long nowMs)`;
  - `TargetEvent OnClick(Pt p, bool onOwnWindow, long nowMs)`;
  - `TargetEvent OnPointer(Pt p, long nowMs)`;
  - `TargetEvent Tick(long nowMs)`;
  - `void Disarm()`.
- `SnapCandidate(Box Bounds, bool Interactive, bool OwnProcess)` and `TargetSnap.Choose(IReadOnlyList<SnapCandidate> chain, Pt centre, Box monitor) -> Box?`. The chain is the element followed by its ancestors. The result is the first candidate that is Interactive, contains the centre, is not our own process, and covers ≤ 25 % of the monitor's area; otherwise null.
- `GuidedTargets.From(Pt centre, Box? snapped, string label, bool isHover) -> GuidedTarget`: radius = clamp(max(w, h) / 2 + 6, 28, 180); with no frame, a 28 px circle.
- `UiaControlSnapper.Snap(int x, int y, TimeSpan budget) -> Box?`:
  - it builds the chain from `AutomationElement.FromPoint`, walking up the parents;
  - the interactive control types are Button, MenuItem, Hyperlink, Edit, ComboBox, CheckBox, RadioButton, TabItem, ListItem, TreeItem, Slider and SplitButton;
  - the budget is 300 ms.
- `GuidedWalkthrough`:
  - `MaximumSteps = 15`: a reply that would start step 16 completes the walkthrough with "That's fifteen steps, so I'll stop here. Ask me again to keep going.";
  - `[GUIDE:hover]` is a step whose target completes after a 2 s hover (new field `bool StepIsHover` on `WalkthroughView`);
  - `EndReason` (string?) is consumed by the next turn's context;
  - `ResponseInstructions` are updated in our own words to mention `hover` and DRAW.
- `TalkOrchestrator` constructor parameter `Func<int, int, Box?>? snapTarget = null`, used to give every mapped POINT a frame through the `PointAt` overload from C4.
- `CompanionCards.Walkthrough(WalkthroughView v) -> NotchCard`:
  - kind `Activity`, id `walkthrough`;
  - title `Step {n}` (or "Guide");
  - body = the instruction (≤ 1,000 chars);
  - actions `back` (only when `CanBack`), `recheck` and `stop` (danger).

**Behaviour (pinned):**
- **Arming:** after each `[GUIDE:step|hover]` reply that pointed, the tray arms the watch with the snapped target.
- **Clicks:** a physical `MouseButtonDown` reads `GetCursorPos` and checks `WindowFromPoint` for our own process.
  - A click inside the frame inflated by `HitSlop` (or within `Radius` of the centre when there is no frame) is **Advance**: after 400 ms the tray runs `MoveWalkthroughAsync(Next)`.
  - A click elsewhere outside our own windows is **LeftGuide**: the walkthrough stops with `EndReason` "the user clicked outside the guided target".
- **Hover:** a 100 ms tick calls `OnPointer`; resting inside the target for 2 s is Advance.
- **Expiry:**
  - `Expired` after 120 s sets the status "Still there? Click the highlighted control or say next step." and keeps the walkthrough;
  - `GuideTimedOut` after 600 s without an advance stops it.
- **App switch:** the foreground moving to another app (not ours) with no Advance in the previous 3 s stops the walkthrough with `EndReason` "the user switched apps".
- **Card actions** route to `MoveWalkthroughAsync(Back/Refresh)` and `StopWalkthrough()`.

- [ ] **Step 1: Write the failing tests.**
  - `WalkthroughTargetsTests`:
    - `ClickInsideInflatedFrameAdvances`;
    - `ClickElsewhereLeavesGuide`;
    - `OwnWindowClickIgnored`;
    - `HoverCompletesAfterTwoSeconds`;
    - `HoverResetsWhenLeaving`;
    - `ArmedTargetExpiresAfter120s`;
    - `GuideTimesOutAfter600sIdle`;
    - `SnapPicksFirstInteractiveAncestor`;
    - `SnapRejectsHugeOrOwnProcess`;
    - `RadiusClamped28To180`.
  - `GuidedWalkthroughTests`:
    - `StopsAtFifteenSteps`;
    - `HoverMarkerSetsHover`;
    - `EndReasonConsumedOnce`;
    - `DrawTagsStrippedFromInstruction`.
- [ ] **Step 2: Run** `dotnet test tests/OpenClicky.Core.Tests --filter "FullyQualifiedName~Walkthrough"`. Expected: FAIL.
- [ ] **Step 3: Implement** the watch, snap, cards, walkthrough changes and tray wiring.
- [ ] **Step 4: Run** the full Core suite and the build. Expected: PASS.
- [ ] **Step 5: Commit** `feat(companion): walkthroughs snap to controls and advance on your click`.

### Task C11: Saved drawings: save offer, chat rows, replay, print

**Files:**
- Create: `apps/windows/OpenClicky.Core/Drawing/DrawingStore.cs`.
- Create: `apps/windows/OpenClicky.Overlay/DrawingReplayWindow.cs`.
- Create: `apps/windows/OpenClicky.App/Companion/TrayApplicationContext.Drawings.cs`, `apps/windows/OpenClicky.App/Companion/DrawingPrinter.cs`.
- Modify: `TalkOrchestrator.cs` (record the turn's lines), `CompanionCards.cs`, `ShellContract.cs` and `contract.ts` (chat row `drawingId`; the page intent `drawing.action`), `IShellHost.cs` (`void DrawingAction(string id, string action)`), `ShellController.cs` (allow-listed routing), `ShellStateBuilder.cs` (`ShellInputs.Drawings`, merged into rows), `tests/fixtures/shell-state.v1.json`, and `apps/companion-workspace/src/shell/notch/ChatTab.tsx`.
- Test: `tests/OpenClicky.Core.Tests/DrawingStoreTests.cs`, additions to `ShellStateBuilderTests.cs`, `ShellControllerTests.cs` and `ShellContractTests.cs`, and vitest in `notch.test.tsx`.

**Interfaces:**
- Consumes: C5's `DrawOp`/`DrawingDocument`, C6's `DrawingRenderer`, C8's `FigureSpec`.
- Produces:
  - `SavedLine(string Spoken, double DurationSeconds, IReadOnlyList<DrawOp> Ops)` with virtual ops.
  - `SavedDrawing(string Id, string Title, DateTimeOffset CreatedAt, IReadOnlyList<SavedLine> Lines, IReadOnlyList<Box> Monitors)`.
  - `DrawingStore(string? folder = null)`:
    - `DefaultFolder` = `%APPDATA%\OpenClicky\drawings`;
    - `string Save(SavedDrawing d)`: atomic temp file + `File.Move(overwrite)`; it throws `InvalidDataException` above 512 KB; at most 200 files are kept and the oldest are pruned;
    - `SavedDrawing? Load(string id)`;
    - `IReadOnlyList<(string Id, string Title, DateTimeOffset CreatedAt)> List()`: newest first; corrupt files are skipped;
    - `bool Delete(string id)`.
  - `TalkOrchestrator.LastDrawingTurn` (`SavedDrawing?`, built when the turn drew at least one part). The title is the first spoken line trimmed to 80 characters, or "Drawing".
  - `CompanionCards.SaveDrawing()`: kind `Tip`, id `drawing-save`, title "Keep this drawing?", body "Save it to replay later, or clear the screen.", actions `save` (primary, "Save") and `clear` (secondary, "Clear"), auto-dismiss after 20,000 ms. Dismissing keeps the drawing on screen unsaved.
  - Contract changes:
    - `chatRow.drawingId?: id`;
    - page → host `{ kind: "drawing.action", id, action: "replayWindow" | "replayFullScreen" | "replayBoard" | "print" | "remove" }`;
    - `ShellInputs.Drawings` (list items as above), merged into chat rows as `Event` rows with the text `Drew {title}` at `CreatedAt`.

**Behaviour (pinned):**
- **Offer:** the save card is offered when a turn that drew ends.
- **Save:** saving stores the drawing and adds its chat row.
- **Clear:** clearing runs `ClearDrawing()`.
- **"Remove from chat":** deletes the file, which removes the row.
- **Replay:** builds a fresh `DrawingDocument` and applies each line in order, speaking the lines when `SpeakReplies` is on (the captions follow B's setting) or pacing by `DurationSeconds` otherwise.
  - **Window** mode: 960×600 DIP centred on the cursor monitor; paper `#FBF6EA`; radius 16; a dark title bar with the title and a close button. It activates so Space can pause or resume.
  - **FullScreen** mode: the parts at their original virtual coordinates over the live screen; click-through.
  - **Board** mode: a full-monitor `#FBF6EA` board with the parts scaled to fit inside 48 DIP margins. It activates.
  - Space pauses or resumes (pausing stops TTS after the current line and holds the pen). Esc closes, and `IsBusy` includes an open replay.
- **Print:** `PrintDialog` + `PrintDocument`. It renders the final parts scaled onto white, with the spoken lines in Segoe Print 11 pt below. Figures print as their label pills only.
- **Privacy:** no screenshots are stored.

- [ ] **Step 1: Write the failing tests.**
  - `DrawingStoreTests`:
    - `RoundTrip` (including a Figure op);
    - `PrunesBeyond200`;
    - `RejectsOver512KB`;
    - `CorruptFileSkippedInList`;
    - `DeleteRemoves`.
  - `ShellStateBuilderTests.DrawingRowsMergedByTime`.
  - `ShellControllerTests`:
    - `DrawingActionRoutedOnlyForKnownActions`;
    - `DrawingActionUnknownIdIgnored`.
  - `ShellContractTests.FixtureWithDrawingRowParses`.
  - Vitest:
    - `ChatTab renders drawing row with Replay, Print, Remove`;
    - `drawing.action posted with mode`.
- [ ] **Step 2: Run** `dotnet test tests/OpenClicky.Core.Tests --filter "FullyQualifiedName~DrawingStore|FullyQualifiedName~ShellStateBuilder|FullyQualifiedName~ShellController|FullyQualifiedName~ShellContract"` and `pnpm --dir apps/companion-workspace test -- notch`. Expected: FAIL.
- [ ] **Step 3: Implement** the store, the contract on both sides, the fixture, `ChatTab`, the replay window, the printer and the tray wiring.
- [ ] **Step 4: Run** both suites and the build. Expected: PASS, 0 warnings.
- [ ] **Step 5: Commit** `feat(drawing): save, replay, print and keep drawings in chat`.

### Task C12: Drive: consent, Esc pill, take-back, elevation refusal

**Files:**
- Create: `apps/windows/OpenClicky.Core/Companion/Drive.cs` (`DrivePhase`, `DriveEnd`, `PhysicalKind`, `DriveSession`, `DriveGuard`, `DriveConsentConfirm`).
- Create: `apps/windows/OpenClicky.Agent/ProcessIntegrity.cs`, `apps/windows/OpenClicky.App/Companion/TrayApplicationContext.Drive.cs`.
- Modify: `CompanionCards.cs`, and `TrayApplicationContext.cs`:
  - `RunManagedTaskAsync`: create the session and pass the `DriveConsentConfirm` to `WindowsDesktopToolHost` and `AgentOrchestrator`, instead of `_confirm`;
  - `ExecuteMappedAsync`: the gate;
  - the physical-input handler;
  - `IShellHost.CardAction` routes.
- Test: `tests/OpenClicky.Core.Tests/DriveSessionTests.cs`, `DriveGuardTests.cs`.

**Interfaces (Produces):**
- `enum DrivePhase { Idle, AwaitingConsent, Driving, TakenBack, Ended }`, `enum DriveEnd { Finished, Declined, UserTookOver, Stopped, SafetyStop, Failed }`, `enum PhysicalKind { MouseMove, MouseButton, Key }`.
- `DriveSession(AgentAccessMode accessSnapshot, Func<CancellationToken, Task<bool>> askConsent)`:
  - `AgentAccessMode Access` (fixed), `DrivePhase Phase`, `DriveEnd? EndedBy`;
  - `Task<bool> EnsureConsentAsync(CancellationToken ct)`:
    - Preview → false, and `askConsent` is never called;
    - it asks at most once per session, and the answer is remembered;
    - "no" or a 60 s timeout → `End(Declined)`;
    - "yes" → Driving;
  - `bool OnPhysicalInput(PhysicalKind kind, int cursorX, int cursorY)`: true = take back. It acts only in Driving: Key or MouseButton → true; MouseMove → true when the distance from the baseline is ≥ `TakeBackDistancePx = 40`. A true result sets TakenBack. In every other phase it returns false.
  - `void NoteAgentCursor(int x, int y)`: resets the baseline after each agent action and at the drive start.
  - `void Resume(int x, int y)`: TakenBack → Driving with a new baseline.
  - `void End(DriveEnd reason)`.
- `DriveGuard.Refuses(int? targetIntegrityRid, int ownIntegrityRid) => targetIntegrityRid is null || targetIntegrityRid > ownIntegrityRid`. RIDs: Low `0x1000`, Medium `0x2000`, High `0x3000`, System `0x4000`.
- `DriveConsentConfirm(IAgentConfirm inner, DriveSession drive) : IAgentConfirm`. `ConfirmAsync` awaits `drive.EnsureConsentAsync` and returns false if consent was declined; otherwise it returns `inner.ConfirmAsync`. `ConfirmNonInputAsync` passes straight through (opening an app is not driving input).
- `ProcessIntegrity.Of(nint hwnd) -> int?` (`GetWindowThreadProcessId` → `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` → `OpenProcessToken(TOKEN_QUERY)` → `GetTokenInformation(TokenIntegrityLevel)`; any failure → null) and `ProcessIntegrity.Own() -> int`.
- Cards:
  - `CompanionCards.DriveConsent()`: kind `NeedsYou`, id `drive-consent`, title "Let OpenClicky use your mouse and keyboard?", body "For this task only. Move the mouse or press any key to take over; Esc stops.", actions `drive` (primary, "Let OpenClicky drive") and `notNow` (secondary, "Not now").
  - `CompanionCards.Driving()`: kind `Activity`, id `driving`, title "OpenClicky is driving", body "Esc stops · move the mouse to take over", action `stop` (danger, "Stop").

**Behaviour (pinned):**
- **Session:** one session per task run, created with the task's access snapshot. `_drive` holds it and is cleared in `finally`, which calls `End(Finished/Stopped/Failed)` and removes the cards. Nothing in C reads or writes `_settings.AgentAccess` during a task, and consent never changes the access mode.
- **Order of gates:**
  - ConfirmEach: consent card (once) → per-action dialog (every action).
  - Auto: the existing plan approval, then consent at the first input action (inside `ExecuteMappedAsync`).
  - Full: consent at the first input action.
  - Preview: never asks and never drives.
- **The `ExecuteMappedAsync` gate** applies to click, type_text, hotkey and scroll only:
  1. `await EnsureConsentAsync` (false → `ActionResult(false, "You chose not to let me drive.")`);
  2. if `DriveGuard.Refuses(ProcessIntegrity.Of(foreground), Own())`, pause the task and return `ActionResult(false, "That window runs as administrator, so Windows won't let me control it. Do this step yourself, then say resume task.")`;
  3. show the `Driving` card once;
  4. execute;
  5. `NoteAgentCursor(GetCursorPos)`.
- **Physical input:**
  - while Driving: `OnPhysicalInput` → `PauseTask()` on true, and the card is replaced by the existing paused progress line; "resume task" or Resume calls `drive.Resume`;
  - while AwaitingConsent: ignored;
  - otherwise: the existing `PauseTaskUnlessConfirming()`.
- **Esc** keeps stopping the task through the existing `IsBusy` path, which ends the session with Stopped.
- **Card actions:** `drive-consent/drive` → true, `notNow` → false (with a 60 s timeout), `driving/stop` → `CancelTask()`.
- **Classic mode** (shell inactive): consent uses `_confirm.ConfirmNonInputAsync` with the same wording, and the overlay bubble shows "driving · esc stops" in place of the pill.

- [ ] **Step 1: Write the failing tests.**
  - `DriveSessionTests`:
    - `PreviewNeverAsksNorDrives`;
    - `AsksOncePerSession`;
    - `DeclineEndsDeclined`;
    - `ConsentTimeoutDeclines`;
    - `AccessSnapshotIsFixed`;
    - `ConsentDoesNotChangeAccessMode` (`Access` is unchanged after "yes");
    - `InputWhileAwaitingConsentIsIgnored`;
    - `JitterUnder40PxIgnored`;
    - `FortyPxTakesBack`;
    - `ClickOrKeyTakesBack`;
    - `BaselineResetsAfterAgentMove`;
    - `ResumeRestartsWatch`;
    - `ConsentConfirmDecoratorAsksConsentBeforeInnerAndSkipsInnerOnDecline`;
    - `NonInputConfirmBypassesConsent`.
  - `DriveGuardTests`: `HigherIntegrityRefuses`, `SameOrLowerAllowed`, `UnreadableIntegrityRefuses`.
- [ ] **Step 2: Run** `dotnet test tests/OpenClicky.Core.Tests --filter "FullyQualifiedName~Drive"`. Expected: FAIL.
- [ ] **Step 3: Implement** the Core types, `ProcessIntegrity`, the cards and the tray wiring.
- [ ] **Step 4: Run** the full Core suite (the existing `AgentAccessPolicyTests` and `CompanionWorkflowTests` stay green) and the build. Expected: PASS, 0 warnings.
- [ ] **Step 5: Commit** `feat(drive): consent card, Esc pill, take-back watch and elevation refusal`.

### Task C13: Native acceptance

- [ ] **Step 1:** Build gate: `dotnet build OpenClicky.sln -c Release` (0 warnings), `dotnet test tests/OpenClicky.Core.Tests` and `pnpm --dir apps/companion-workspace test` are all green, and `dotnet run --project apps/windows/OpenClicky.Overlay.Smoke` passes.
- [ ] **Step 2:** Run the built app with "Show in screen recordings" on, so the overlay appears in screenshots (HeyClicky's cursor overlay could not be captured in the live notes). Then check, taking a screenshot for each:
  - the triangle in each of the 4 colours follows on a spring (Settings → Cursor);
  - holding talk shows the bars, then the thinking dots, then the message bubble word by word;
  - Hide floating cursor docks it into the notch, and turning it off brings it back;
  - "point at the start button" flies the cursor and shows the dashed ring with a label pill;
  - "draw a circle around the clock and label it" draws in time with speech, and "make it red" recolours the same id;
  - "show me a gear in 3D" shows a lit spinning figure;
  - "show me how to change the wallpaper" snaps a ring to a real control, and clicking it advances;
  - the save card stores a drawing, the Chat row offers Replay (each of the three modes), Print preview and Remove;
  - an Agent task in ConfirmEach shows the consent card, then the per-action dialog, then the Driving pill.
- [ ] **Step 3:** Side-by-side: save matching OpenClicky screenshots next to the HeyClicky ones in `docs/heyclicky-parity/compare/` (`c1-cursor-*.png`, `c2-ring.png`, `c3-drawing.png`, `c4-figure.png`, `c6-walkthrough.png`, `c7-replay.png`, `c8-drive.png`).
- [ ] **Step 4: User-only physical checks.** These need real hands, so put them on the user's phone list:
  1. Shake the mouse: the cursor stays put; clicking it within 3 s starts talking.
  2. Hold Ctrl+Alt and circle something while asking "what is this?": the answer refers to the circled item.
  3. While driving, nudge the mouse a little (< 40 px): it keeps going; then move it well past that: it pauses; "resume task" continues; Esc stops.
  4. With an administrator app in front (for example an elevated terminal), an Agent click is refused with the administrator message.
  5. Click outside the walkthrough ring: the guide ends; clicking the card's Stop or Back does not end it as "clicked elsewhere".
  6. Print a saved drawing to a real printer or PDF.
- [ ] **Step 5:** Update ledger rows C1–C8 (Built → Native-accepted, with evidence paths), add the Skip rows from this plan with their reasons, and update `docs/feature-plan.md`.

---

## Self-review notes

- **Spec coverage (§C):**
  - Cursor: spring, bars, dots, message and label bubbles → C1–C2; dock, hide and shake → C3.
  - Drawing: full primitives and helpers → C5–C7, except the explicit Skips; Direct3D figures → C8 through WPF (Direct3D-backed, no new package; this deviation from a GPU HLSL renderer is listed in the Skip table).
  - Gestures with stroke classification and UIA text → C9.
  - Walkthroughs snapping to UIA and advancing on a real click → C10.
  - Saved drawings with replay → C11; video export is a Skip with its reason.
  - Drive: consent plus Esc pill, layered on access modes, with ConfirmEach still per action → C12.
- **Type names used across tasks:**
  - `Pt`/`Box` (C1) everywhere in new Core code;
  - `DrawOp`/`DrawPart`/`PartAnimation`/`DrawingDocument` (C5), consumed by C6–C8 and C11;
  - `FigureSpec` (a C5 placeholder, filled by C8);
  - `ClickCatcher` (C3), reused by C7;
  - `CompanionCards` (C10), extended by C11–C12;
  - `IOverlayController` additions (`ShowIdle`, `ApplyStyle`, `ShowMicLevel`, `WindowHandles`, the `PointAt` overload, `ShowDrawing`, `FinishDrawing`, `HasDrawing`, `AddHighlightPoint`, `ClearHighlight`, `SetNotchPoint`), all with no-op defaults so the existing fakes compile.
