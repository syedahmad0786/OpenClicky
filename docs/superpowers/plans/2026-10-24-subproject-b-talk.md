# Sub-project B — Talk: shortcuts, text mode, voices, captions, capture, talk actions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Talk to OpenClicky the HeyClicky way:
- hold Ctrl+Alt to talk;
- double-tap Ctrl to type;
- hear replies in a chosen voice (Windows voices offline, or OpenAI voices with the user's own key) at a chosen speed on a chosen output device, with word-by-word captions;
- get muted-speaker and microphone-trouble cards;
- send every display to the model each turn, never including OpenClicky's own windows;
- let replies open links, copy text or type into the focused field under the user's access mode.

**Architecture:**
- A pure `ShortcutStateMachine` (Core, fully unit-tested) turns low-level key events into actions. `ShortcutMonitor` (Input) hosts it on the existing `WH_KEYBOARD_LL` hook and replaces `PushToTalkHotkeyMonitor`, still implementing `IPushToTalkMonitor` for talk.
- Speech output moves to a `SpeechOutput` (App) that plays Windows SAPI or OpenAI TTS audio through NAudio on the selected render device and raises word events for captions.
- Capture for Talk uses all monitors through an `OwnWindowsExcludedCapture` wrapper.
- Talk actions are in-band reply tags parsed in Core (`TalkActionParser`), the same pattern as `[POINT]`/`[MARK]`, executed in App under `AgentAccessPolicy`.

**Tech Stack:** as before, plus NAudio 2.2.1 (already referenced) for render devices and playback, System.Speech for SAPI, and HttpClient for OpenAI TTS.

**Spec:** [design spec](../specs/2026-10-24-heyclicky-parity-design.md) §B · ledger rows B1–B8.

## Current code facts (from the survey)

- **`PushToTalkHotkeyMonitor` (Input):**
  - Mechanics: `WH_KEYBOARD_LL` on the UI thread; events Pressed/Released/Cancelled (Cancelled fires on any Esc). It matches `HotkeyChord`, which requires a non-modifier key, has no timing, swallows only the main key, and passes injected input (flag 0x10) through.
  - Lifecycle: started by `TalkOrchestrator.Start()`; Pause calls Stop/Start in `TrayApplicationContext.Shell.cs` `SetPaused`.
  - Consumers: `TalkOrchestrator.OnPressed` → BeginListening, `OnReleased` → EndListeningAsync (async void). The tray also handles Pressed (cancel submission, pause task) and Cancelled (Stop).
- **Dictation** is started only from menus (`BeginDictationAtCurrentTarget` → `StartDictation` → `_talk.StartConversation()`).
- **"sub-project B:" markers** are in `HotkeyBinding.LiveTalk`, `SettingsPatch` (talk slot + TalkLocked), `ShellSettingsBuilder` (talk view) and the `AppSettings.TalkHotkey` comment.
  - `ShellStateBuilder` emits `ShellHotkeys(Keys(PushToTalk), [], [], [])`.
  - Tests that pin Win+Alt+Space: `HotkeyChordTests`, `HotkeySettingsPersistTests`, `ShellStateBuilderTests:120`, `ShellSettingsBuilderTests:27`, `SettingsPatchTests:118`, `tests/fixtures/shell-state.v1.json`, and the SettingsForm strings.
  - Fakes of `IPushToTalkMonitor`/`IScreenCaptureService` in `CompanionWorkflowTests`/`AgentAccessPolicyTests`: add members with default implementations or update the fakes.
- **TTS today:** `ITtsClient { SpeakAsync(text, ct); Stop(); }`. `LocalSpeechTtsClient` creates a SAPI `SpeechSynthesizer` per call with a fixed Rate = 2 on the default device; it has no voice selection, no word events, and nothing plays through NAudio.
  - Sentence-by-sentence speech goes through `StreamingReplySpeech`.
  - Barge-in works only in conversation mode with local Whisper and the echo-cancelled recorder.
- **Capture:** Talk currently captures only the foreground window (`CaptureRegionJpegAsync`, always GDI). `AgentOrchestrator` uses `CaptureAllMonitorsJpegAsync` (WGC, cursor monitor first, max edge 1280, GDI fallback). The prompt and `CoordinateMapper` already support `:screenN`. The orchestrator hides the buddy overlay before capture.
- **Window handles:** the overlay exposes `WindowHandle` and `BuddyTipHandle` (the tip is created lazily). `GeneralBehaviour.ApplyCaptureExclusion(hwnd, bool)` exists; `ShellSurfaceWindow.ExcludeFromCapture` exists.
- **Shell:** `ShellController.OpenNotch(tab)`, `AddToast`, cards (MutedSpeaker/MicTrouble kinds exist); `card.action` currently only removes the card. TS has host→page `caption.word {text ≤ 200}` and `mic.level`, but nothing in C# sends them.
- **Microphone level:** only `NAudioMicrophoneRecorder.PeakLevel` (concrete class) and the static `ListDevices()`.
- **Secrets:** `ISecretStore` Get/Set/Delete; `SecretNames` has OpenRouterApiKey and ChatGptOAuth.

## Global Constraints

- Everything from sub-projects A and S.
- **Default shortcuts:** talk = hold Ctrl+Alt; text mode = double-tap Ctrl; dictate = hold Ctrl+Win; hands-free dictation = double-tap Ctrl+Win.
- **Shortcut timings:** a tap is ≤ 0.4 s; a double-tap is two taps within 0.5 s; the recorder times out after 7 s; Esc cancels.
- **Shortcut safety rules:**
  - Injected input (LLKHF_INJECTED 0x10) never triggers anything.
  - A modifier-only chord never fires if any non-modifier key is pressed while it is down, so Ctrl+C, Ctrl+Alt+Del and AltGr+letter are unaffected.
  - When a chord containing Win completes, the Start menu must not open (mask with an unassigned VK 0xE8 down/up before the Win key-up passes).
- **Speed and voices:**
  - Speeds are 0.5, 0.75, 1, 1.25 and 1.5; SAPI rate = round((speed − 1) × 10), clamped to −10…10.
  - OpenAI voices: alloy, ash, ballad, cedar, coral, echo, marin, sage, shimmer, verse.
  - TTS endpoint `https://api.openai.com/v1/audio/speech`, model `gpt-4o-mini-tts`, response format `wav`, bearer = the user's key from Credential Manager (`SecretNames.OpenAiApiKey = "openai-api-key"`).
  - The key never leaves the host, is never logged and never enters state.
- **Line queue:** replies are spoken sentence by sentence with a 0.35 s gap; Stop and barge-in cancel immediately; if audio fails, the text reply still shows.
- **Captions:** shown only when the `voice.captions` setting is on; word by word; bottom centre; cleared 1.5 s after speech ends.
- **Capture:** all monitors per Talk turn (cursor monitor first), after a 120 ms settle. OpenClicky's own windows are excluded via `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` for the duration of the capture, then restored to the user's "show in recordings" choice. Selected-region asks are unchanged.
- **Talk action tags:**
  - Formats: `[OPEN:<http(s) url>]`, `[COPY:<text ≤ 2,000>]`, `[TYPE:<text ≤ 2,000>]`; at most 3 per reply; stripped from spoken and displayed text.
  - OPEN accepts only http/https.
  - TYPE goes through the existing verified insertion into the focused field and refuses password fields.
  - Preview mode never executes and shows a card with the action.
  - ConfirmEach asks before OPEN and TYPE; COPY runs without asking.
  - Auto and Full follow `AgentAccessPolicy`.
- **Muted speaker:** checked before speaking; if the default/selected render endpoint is muted or at volume 0, show a `mutedSpeaker` card with "Turn volume to 50%" and copy the reply to the clipboard.
- **Mic trouble:** if a hold-to-talk lasting ≥ 3 s has peak < 0.01 throughout, show a `micTrouble` card with "Microphone settings" (opens Settings → Microphone).

## Review Focus

- Ctrl+C, Ctrl+V, Ctrl+Alt+Del, AltGr+e and Win+L must not trigger talk/dictate/text mode. Test: `ShortcutStateMachineTests.ModifierOnlyChordsIgnoreOrdinaryShortcuts`.
- The agent's own injected keystrokes must never start voice. Test: `ShortcutStateMachineTests.InjectedInputIsIgnored`.
- Releasing Win after Ctrl+Win dictation must not open the Start menu. Test: `ShortcutStateMachineTests.WinChordRequestsStartMenuMask` (the command is emitted before Win-up passes); native check in B7.
- A user who customised the old Win+Alt+Space chord keeps a working talk shortcut after upgrading. Test: `HotkeyMigrationTests.CustomPushToTalkMigratesToTalkHotkey`.
- A reply containing `[TYPE:…]` in Preview mode types nothing, and in ConfirmEach types only after approval. Test: `TalkActionPolicyTests`.

---

### Task B1: Shortcut state machine, monitor, migration

**Files:**
- Create: `apps/windows/OpenClicky.Core/Input/ShortcutStateMachine.cs`, `apps/windows/OpenClicky.Input/ShortcutMonitor.cs`.
- Modify: `TrayApplicationContext.cs` (construct ShortcutMonitor; wire events), `SettingsPatch.Normalize`/`SettingsStore` (migration), and remove every `// sub-project B:` guard in Core/Settings so `shortcuts.talk` becomes editable and the view shows `TalkHotkey`.
- Modify `ShellStateBuilder` so `hotkeys.talk/text/dictate/handsFree` come from the four bindings.
- Modify `SettingsForm.cs`: its push-to-talk capture box becomes a read-only line "Change shortcuts in Settings → Shortcuts".
- Tests: `ShortcutStateMachineTests`, `HotkeyMigrationTests`; update tests that pin Win+Alt+Space.

**Interfaces:**
- `ShortcutStateMachine(ShortcutBindings bindings)`.
  - Input: `IReadOnlyList<ShortcutCommand> OnKey(int vk, bool down, bool injected, long timestampMs)`.
  - Commands: `TalkStart`, `TalkEnd(cancelled)`, `TextMode`, `DictateStart`, `DictateEnd(cancelled)`, `HandsFreeToggle`, `Cancel`, `MaskStartMenu`.
  - Recording: `BeginRecording(ShortcutAction action, long nowMs)` → `HotkeyBinding? Recorded` after the chord completes, or `RecordingTimedOut` after 7 s.
- `ShortcutMonitor : IPushToTalkMonitor`. Pressed/Released = talk; new events `TextMode`, `DictateStarted`, `DictateEnded`, `HandsFreeToggled`; `Bindings` is settable; `Record(ShortcutAction, Action<HotkeyBinding?>)`.
- Hold semantics: an action starts the moment its exact key set is down (talk starts recording immediately). If released within 0.4 s it counts as a tap: the hold action ends with `cancelled: true` and the tap counts toward a double-tap of the same key set.
- Migration: if `PushToTalk` ≠ `HotkeyChord.Default` and `ShortcutsVersion` < 2, set `TalkHotkey` = Hold binding of that chord's keys, then set `ShortcutsVersion = 2` (new AppSettings int, default 0 when absent).
- [ ] Tests from Review Focus 1–4 plus hold/tap/double-tap timing, Esc cancel, and recorder capture/timeout.
- [ ] Commit `feat(talk): modifier-only shortcuts with hold and double-tap`.

### Task B2: Dictation and text-mode wiring

**Files:** Modify `TrayApplicationContext.cs` (DictateStarted → existing `BeginDictationAtCurrentTarget` with auto-insert on release; HandsFreeToggled → start/stop the same flow without release; TextMode → `ShellController.OpenNotch(Chat)` + `ActivateForInput()` + post `composer.focus`), `ShellController.cs` (add `PostComposerFocus()`), `contract.ts` + `ShellContract` (host→page `composer.focus {}`), and `ChatTab.tsx` (focus the textarea on `composer.focus`). Also S3's `recordHotkey`/`resetHotkeys` handlers now use `ShortcutMonitor.Record` for all four rows. Tests: vitest for `composer.focus`; Core test for the host message.

- [ ] Commit `feat(talk): dictation and text-mode shortcuts`.

### Task B3: Speech output, voices, OpenAI key

**Files:**
- Create: `apps/windows/OpenClicky.App/Speech/{SpeechOutput,WindowsVoice,OpenAiVoice,RenderDevices}.cs`, `apps/windows/OpenClicky.Core/Speech/{SpeechPlan,VoiceCatalog}.cs`.
- Modify: replace `LocalSpeechTtsClient` usages with `SpeechOutput : ITtsClient` exposing `event Action<string> WordSpoken` and `event Action SpeechEnded`.
- Modify: S3's `SettingsRuntimeProbe` (render devices via NAudio `MMDeviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, …)`, OpenAI key set flag), S3's `secret.edit` (arg `openai`), `previewVoice` and `playTestSound` handlers.
- Modify: `ISecretStore` `SecretNames` (+`OpenAiApiKey`).
- Tests: `SpeechPlanTests` (sentence split, rate mapping, gap timing), `VoiceCatalogTests`.

**Behaviour:**
- WindowsVoice: SAPI `SelectVoice` by token id, mapped from the short voice id (S3); rendered with `SetOutputToWaveStream` into memory; played with `WaveOutEvent` on the selected device (default device when unset or unavailable); `SpeakProgress` drives word events.
- OpenAiVoice: POST with a 30 s timeout; the WAV is played the same way; words are spread evenly across the audio duration.
- previewVoice speaks "Hi! This is how I sound." with that voice.
- playTestSound plays a 0.3 s 880 Hz tone on the selected device.

- [ ] Commit `feat(talk): voices, speed and output device`.

### Task B4: Captions, muted speaker, mic trouble

**Files:**
- Modify `ShellController.cs`: `PostCaption(string word)`, `ClearCaptions()`, `AddCard(NotchCard)`, `RemoveCard(id)`, and card-action routing to `IShellHost.CardAction(cardId, actionId)`.
- Create `src/shell/captions/CaptionsBar.tsx` (bottom-centre pill, max 2 lines, own `data-region`, fades) and mount it in ShellApp.
- Modify TrayApplicationContext (wire `WordSpoken` → captions when the setting is on; muted check via `MMDevice.AudioEndpointVolume` before speaking; mic-trouble detector using `NAudioMicrophoneRecorder.PeakLevel` sampled every 100 ms during hold).
- Tests: CaptionsBar vitest (words append, clear); Core test for card routing; `MutedSpeakerPolicy` pure test.

- [ ] Commit `feat(talk): captions, muted-speaker and microphone cards`.

### Task B5: Multi-display capture without our own windows

**Files:**
- Create `apps/windows/OpenClicky.App/Capture/OwnWindowsExcludedCapture.cs` (implements `IScreenCaptureService` by wrapping WGC).
- Modify `TalkOrchestrator.cs`: Talk uses `CaptureAllMonitorsJpegAsync` with a 120 ms settle. The foreground guard compares only the foreground handle before and after. Keep the region path.
- Collect handles: shell surface, overlay, tip, dock, walkthrough, dictation panel.
- Tests: Core test of the orchestrator with a fake capture verifying all-monitor capture is requested, and the region path is unchanged.

- [ ] Commit `feat(talk): capture every display, excluding OpenClicky windows`.

### Task B6: Talk actions

**Files:**
- Create `apps/windows/OpenClicky.Core/Talk/{TalkActionParser,TalkActionPolicy}.cs`.
- Modify `CompanionPrompts.cs` (document the three tags in our own words), `TalkOrchestrator.cs` (parse and strip before speaking, raise `ActionsRequested`), and `TrayApplicationContext.cs` (execute via `AgentAccessPolicy` + `WinFormsAgentConfirm`; TYPE via the existing dictation insertion; OPEN via ShellExecute for http/https only; COPY via Clipboard).
- Tests: `TalkActionParserTests` (caps, stripping, malformed tags, non-http rejected), `TalkActionPolicyTests` (Preview/ConfirmEach/Auto/Full).

- [ ] Commit `feat(talk): open, copy and type actions from replies`.

### Task B7: Native acceptance

- [ ] Run the built app and check each of these:
  - Hold Ctrl+Alt: the buddy shows listening, and release sends.
  - Double-tap Ctrl: the notch opens with the composer focused (type nothing into other apps).
  - Ctrl+C in Notepad doesn't trigger anything.
  - Ctrl+Win hold then release doesn't open Start.
  - Preview voice plays on the selected device; captions appear while speaking.
  - Muted speaker shows the card.
- Record evidence in the ledger; update the B1–B8 rows.
