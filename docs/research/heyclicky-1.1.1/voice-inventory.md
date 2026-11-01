# HeyClicky 1.1.1 (Windows): conversation, voice and on-screen companion inventory

Clean-room feature inventory for OpenClicky. This is a static read only. Nothing was launched and no server was contacted. Everything below is described in our own words. Short labels and identifiers appear only as evidence. No code, prompt text, sounds or art were copied.

## Method and evidence

- Target: `%LOCALAPPDATA%\HeyClickyApp\current`. Only metadata and asset file names/sizes were read. The JS engine files under `Assets\Engine` were read only for their header comments and identifier names.
- REA evidence:
  - `HeyClicky.Core.dll` (sha256 `3e947a98…`): REA admission failed, `ev_f570414d…` (parent `ev_d2484678…`, "metadata exceeds 64 MB budget"). I read it with System.Reflection.Metadata instead.
  - `HeyClicky.Drawing.dll` (`7ee5b461…`): members `ev_14ca02c9…` (parent `ev_eb5abb0b…`), complete.
  - `HeyClicky.Drawing3D.dll` (`fd8a44a0…`): members `ev_a1f90040…` (parent `ev_fa33edef…`), complete.
  - `HeyClicky.Platform.dll` (`1b4230d4…`): native boundaries `ev_067d6326…`, 17 module refs and 197 P/Invoke imports. The earlier 6.5.0 IDs for Platform were `ev_67843814…` (members) and `ev_8b118a97…` (boundaries).
- My own static tooling read `HeyClicky.dll` (`1ed48ccf…`), `HeyClicky.Core.dll` and `HeyClicky.Contracts.dll` (`8f418865…`). It did not load any target code. It collected:
  - type and member names;
  - the `#US` literals loaded by each method body (an `ldstr` scan, so attribution is heuristic);
  - `const` field values;
  - ImplMap P/Invoke rows;
  - IL bytes for one small static constructor.
- Evidence notation: `Type::Member` is a type or member name. "lit" means a string literal found in that method. "const" means a constant field value.
- **Observed** means present in shipped metadata, strings or constants. **Inferred** means reasoned from names; it was not seen running.

---

## 0. Local models, ONNX and DirectML

- **Observed:** `onnxruntime.dll` (21.7 MB), `Microsoft.ML.OnnxRuntime.dll`, `DirectML.dll` (18.7 MB), `NPUDetect.dll` and many `Microsoft.Windows.AI.*` DLLs are shipped.
- **Observed:** No HeyClicky assembly references OnnxRuntime, `Microsoft.Windows.AI.*`, `Windows.Media.SpeechRecognition` or `Windows.Media.Ocr`. The checks covered AssemblyRef and TypeRef in HeyClicky, Core, Platform, Drawing, Drawing3D and Agents.
- **Observed:** They arrive through the `Microsoft.WindowsAppSDK` 2.5.1 metapackage (`.AI` 2.5.5, `.ML` 2.1.94) in `HeyClicky.deps.json`.
- **Observed:** No model files ship anywhere in the install. A search for `.onnx`, `.ort`, `.gguf`, `.bin`, `.tflite` and `.pt` found none.
- **Conclusion (inferred):** These are unused transitive payload. All speech recognition, speech output and the "brain" run in the cloud.
- **Observed:** Bundled media relevant here (names/sizes only):
  - `Assets\VoicePreviews\realtime-voice-preview-{alloy,ash,ballad,cedar,coral,echo,marin,sage,shimmer,verse}.mp3`, about 42–74 KB each;
  - `Assets\Earcons\*.wav` (17 files, for example agent-done, agent-needs-you, clicky-question, clicky-text-open/send/receive/close, hatching 1.1 MB);
  - `Assets\Sounds\*.wav` (about 70 drawing sound effects such as pen-flick, highlighter, stamp, sticky-press, whoosh, plus 285 KB looping "beds" for handwriting, eraser and pencil);
  - fonts `ClickyDraw-PatrickHand.ttf` and `ClickyDraw-Barlow-*.ttf`;
  - `Assets\Engine\clicky-draw-engine.js` (260 KB), `clicky-draw-native.js`, `clicky-draw-pieces.js`, `clicky-three.js` (three.js 0.186.1 slice) and `clicky-three-parts.js`.
- **Observed:** The JS runs in embedded V8 through `Microsoft.ClearScript.V8` 7.5.1.1, used by Drawing and Drawing3D (`NativeEngine::DisableJit`, setting `DrawingJavaScriptJit`). There is no WebView for drawing.

## 1. Talk input: push-to-talk, hands-free, wake, hotkeys, barge-in, AEC, VAD, devices

**Hotkey model**
- **Observed:** The enum `HeyClicky.ShortcutAction` (Contracts const) has these values: PushToTalkDown/Up, TextComposerToggle, DictationDown/Up, Escape, DictationCancel, HandsFreeDictationToggle, ProactiveDebugSweep, MorningPreviewChord, SystemScreenshotShortcut.
- **Observed:** A binding is a modifier set (`HotkeyModifiers` Ctrl=1, Alt=2, Shift=4, Win=8) plus a trigger (`HotkeyTrigger` Hold=0, DoubleTap=1). Bindings are modifier-only, and the display text uses "pressed twice" for double-tap.
- **Observed:** Default bindings, decoded from the IL of `HotkeyBinding::.cctor`, which constructs `(mods, trigger)` four times:

  | Action | Default |
  | --- | --- |
  | Push-to-talk | hold **Ctrl+Alt** |
  | Text composer | **double-tap Ctrl** |
  | Dictation | hold **Ctrl+Win** |
  | Hands-free dictation | **double-tap Ctrl+Win** |

  The onboarding strings in Core confirm "hold ctrl + alt".
- **Observed:** Tap timing: `ShortcutMonitor::ControlTapMaxHoldMs`=400 and `ControlTapMaxIntervalMs`=500 (const). The recorder in `App.Settings.ShortcutRecorder` uses TapMaxHoldSeconds 0.4, DoubleTapMaxIntervalSeconds 0.5 and TimeoutSeconds 7. Recorder slots are PushToTalk, TextMode, Dictation and HandsFreeDictation.
- **Observed:** `ShortcutMonitor` handles these edge cases:
  - AltGr disambiguation (`leftControlBelongsToAltGr`, `rightAltIsAltGr`);
  - masking the Start menu so that Win alone does not open it (`MaskStartMenu`, `StartMenuMaskVirtualKey`=255, `SendMaskKey`);
  - swallowing Escape;
  - a hold watchdog;
  - resync from async key state.
- **Observed:** If Ctrl+Win plus another key is pressed during dictation, dictation is cancelled because that combination is a Windows shortcut (lit in `DictationController::HandleShortcut`).
- **Observed:** Key capture uses `LowLevelInputHub`:
  - a shared WH_KEYBOARD_LL hook on its own thread, plus Raw Input (`RegisterRawInputDevices`, `GetRawInputData`) as a cross-check;
  - a watchdog that reinstalls the hook when Raw Input sees keys the hook missed (lit: "no key reached the hook in 3 s while Raw Input saw some", and a note when the window in front runs elevated);
  - a signature that marks the app's own injected input so it can be ignored (`HeyClickyInjectedInputSignature`=1212369995).

**Push-to-talk turn**
- **Observed:** Entry points are `CompanionSession::BeginPushToTalkAsync` and `EndPushToTalkAsync`. The microphone runs only while the keys are held. Audio for the press is also kept in a buffer of up to 120 s (`MaximumPressAudioSeconds`=120).
- **Observed:** Presses that are too short are cancelled (lit "too short, cancelled"). A hold with no microphone audio produces a "can't hear your microphone" message. Repeated silent presses are tracked (`consecutiveDeadAirPresses`).
- **Inferred:** There is a short tail after key release (`ReleaseTail` field).
- **Hands-free for talk:** none observed. Hands-free exists only for **dictation**: double-tap locks it (`DictationEngagement` Idle/Holding/HandsFreeLocked, `DictationHotkeyStateMachine::LockDoubleTapMaxGap`, `QuickTapMaxHold`, `ToggleHandsFree`).
- **Wake word:** none observed. The only "hey clicky" literal is in `BugReport::Run`. No keyword-spotting model or API is present.
- **Typed turns:** double-tap Ctrl opens the notch text composer (`NotchChatComposer`, `CompanionSession::SubmitTextAsync`). The setting `TypedTurnsRespondWithVoice` decides whether typed turns are answered aloud or in text, and a read-aloud switch exists in the notch (`ReadAloudSwitch`).

**Barge-in and interruption**
- **Observed:** The names `CompanionSession::InterruptForNewInput`, `Cancel`, `turnCancellation` and `RealtimeMouth::Interrupt` exist. In the engine, `L.pause()` and `L.resume()` hold the drawing pen after the current stroke; the header comment calls this a barge-in.
- **Inferred:** Barge-in works by pressing the talk keys again. The new press cancels the in-flight brain turn, stops speech playback and pauses the drawing, which is then continued or restarted (`DrawingContinuationDecision` BuildOn/StartOver, the `startOver(` marker). Interruption by voice is not supported, because the microphone is closed while the app speaks.
- **Observed:** Clicks during a turn are handled too. `CompanionSession::OnCanvasUserActedOnScreen` reacts to them, and tool calls the user moved past are dropped ("moved on before this ran").

**AEC and VAD**
- **Observed:** There is no echo cancellation, DSP or voice-capture effect. The code uses plain WASAPI shared mode only (`IAudioClient`, `IAudioCaptureClient`, `IAudioClock`, `IMMDevice*`, `IAudioEndpointVolume`, `IAudioMeterInformation`). There is no IAudioClient2/AudioClientProperties communications category and no VAD library.
- **Inferred:** No AEC is needed because the design is push-to-talk with half-duplex playback.
- **Observed:** Something close to VAD exists in two places:
  - a microphone level meter (`WasapiMicrophoneCapture::UpdateLevel`, `smoothedLevel`), used for the waveform bars;
  - a liveness check that sees "no audio from mic" and rebuilds or fails over: `LivenessCheckMilliseconds`=500, `StallThresholdMilliseconds`=1000, `MaximumStallRebuilds`=3, `RebuildCooldownMilliseconds`=1500. After repeated failures it switches to the built-in microphone (`FailOverToBuiltInMicrophone`, `MicrophoneProvedDead`).

**Devices**
- **Observed:** Capture format is 24 kHz mono PCM16 in 20 ms frames (`CaptureSampleRate`=24000, `FrameDurationMs`=20, `SamplesPerFrame`=480). The device is selected by endpoint ID (`AudioDevices::PreferredCaptureId` / `PreferredRenderId`, settings `MicrophoneDeviceId` and `OutputDeviceId`, "System Default").
- **Observed:** Default-device changes are followed (`DefaultDeviceChangeListener`, `AudioTopologyMonitor` debounce 400 ms).
- **Observed:** Bluetooth handling:
  - if the system default microphone is Bluetooth, the app uses the built-in microphone so the headset stays in A2DP (lit in `AudioDevices::Resolve`);
  - a held Bluetooth microphone is kept open until the reply finishes playing (`HoldBluetoothMicrophoneOpenUntilTheReplyPlaysOut`, `StopKeepingDeviceOpen`).
- **Observed:** Windows privacy blocks are distinguished (`MicrophoneBlock` None/Policy/Device/AllApps/DesktopApps; ConsentStore registry path const).
- **Observed:** `QuietContext::IsAnotherAppUsingMicrophone` and DND/fullscreen detection (`SHQueryUserNotificationState`, `NtQueryWnfStateData` quiet-hours) gate proactive speech.
- **Observed:** Speaker mute is detected: `OutputSilenceKindNow`, the onboarding `MutedOutputVolumeThreshold`=0.02 and `SystemVolume::EffectiveLevel`. When muted, the spoken answer is copied to the clipboard and a notch card says so (`PresentMutedSpeakerReply`, `NotchLiveEvent+VoiceReplyMutedCopiedToClipboard`).

## 2. Speech-to-text and dictation into other apps

**Companion "ears" (talk STT)**
- **Observed:** `DeepgramTranscriber` (Area "Ears") streams to Deepgram over a websocket with linear16 mono audio. It requests smart formatting, interim results, filler words, a language and repeated `keyterm` params (lit in `BuildListenUrl`). The model literal is `nova-3`.
- **Observed:** The client never holds a Deepgram key. A short-lived token is minted from the HeyClicky backend per purpose (`/v2/dictation/deepgram-token?purpose=`). A "paywall" reply means the plan limit has been reached.
- **Observed:** Keep-alive and close-stream messages exist, along with partial and final transcripts (`PartialTranscriptSoFar`, `FinalTranscript`).
- **Observed:** A live transcript is shown while the user talks (`StartLiveTranscriptLoop`, `UserTranscriptUpdated`).
- **Observed:** Warm ears: a socket is opened ahead of the press, with an expiry (`PrepareWarmEars`, `warmEarsExpiresTicks`, `TakeWarmEars`, `DeepgramTranscriber::Adopt`).
- **Observed:** Keyterms come from the user's vocabulary (`NormalizedKeyterms`).
- **Observed:** Fallback: if Deepgram fails, the buffered press audio goes to a batch endpoint (`TranscribeFallbackAsync`, lit `/brain/ears/transcribe`, "the press was heard by the fallback").

**Dictation (separate pipeline, Area "Dictation")**
- **Observed:** Flow in `DictationController`, in order:
  1. Hotkey down: capture the focused field over UIA (`DictationTextTarget::CaptureFocusAsync`).
  2. Read app name, site host, text before and after the caret, selected text and whether the field is single-line. Surrounding text is capped at 2000 characters (`SurroundingTextCap`=2000).
  3. Stream to Deepgram on a warm or cold socket.
  4. On release, call cleanup at `/v2/dictation/cleanup` with transcript, audio seconds, context, a fast-cleanup flag and STT ms.
  5. Insert the result.
- **Observed:** If streaming is unavailable, batch mode sends WAV base64 to `/v2/dictation/transcribe` (`RunBatchAsync`, up to 360 s, `MaxAccumulatedSeconds`).
- **Observed:** Cleanup / rewrite mode: the server cleans up the raw transcript using the app/field context (`DictationCleanupContext`: AppName, SiteHost, BeforeText, AfterText, SelectedText, SingleLine). If cleanup fails, the raw transcript is inserted. No user-selectable style or tone presets for dictation were found.
- **Observed:** Insertion (`DictationTextTarget::InsertAsync`, `TextInsertion`):
  - the app snapshots the clipboard, sets the text, sends Ctrl+V with `SendInput`, then polls UIA to verify the text landed (`PasteTextVerifyingLandingAsync`, `LandingPollDelaysMs`);
  - it restores the clipboard after a delay;
  - it normalises em-dashes and leading commas, and spacing for scripts written without spaces;
  - it waits for Win/Alt/Shift to be released (gives up after 3 s).
- **Observed:** Blind paste is used for apps without usable UIA (`BlindPasteProcesses`: WeChat, Teams, ChatGPT, Codex, Doubao, Sublime…). Terminals get special handling (`TerminalProcesses`: Windows Terminal, conhost, pwsh, wezterm, alacritty, kitty, warp…).
- **Observed:** Insertion is refused for password fields, a lost focus, held modifiers and empty transcripts. In those cases the text goes to the clipboard (`DictationInsertOutcome`, `DictationDeliveryOutcome` Inserted/PastedBlind/Clipboard).
- **Observed:** Vocabulary: up to 50 custom words of 60 characters each, plus up to 12 context keyterms per session (`DictationVocabulary`). The account name is seeded into it.
- **Observed:** Learned corrections: after insertion, the app polls the field. If the user edits one distinctive word, it learns the pair by Levenshtein similarity and adds it to the vocabulary (`DictationCorrectionDetector`, `DictationLearnedCorrection`, `NotchLiveEvent+DictionaryWordLearned`, `RemoveLearnedWord`).
- **Observed:** A local transcript archive is kept for 14 days (`DictationTranscriptArchive::RetentionDays`=14).
- **Observed:** Languages: `multi` (multilingual) or a specific code, default `en`. The picker has about 50 languages (`DictationLanguageCatalog`, settings literals).
- **Observed:** A word limit for dictation exists (lit "dictation word limit (402)", `DictationPaywallReachedException`).

## 3. Speech output ("mouth") and realtime voice

- **Observed:** `RealtimeMouth` (Area "Mouth") opens a websocket to the OpenAI Realtime API (lit `wss://api.openai.com/v1/realtime?model=`). The backend mints a client secret (`/agent/realtime/session`, `ParseMint` reads `client_secret`). The default model const is `gpt-realtime-2.1-mini`.
- **Observed (key design):** The realtime model is used as a **TTS-only voice**, not as the brain.
  - Each line the brain writes is sent as its own response (`response.create`, conversation `none`, audio-only output) with fixed "read this line verbatim" instructions (`VerbatimNarrationInstructions` const, `BuildVerbatimResponseCreate`) and metadata `clicky_line`.
  - The app tracks first-audio ms per line.
- **Observed:** Line pipelining:
  - lines are queued and pre-rendered (`queuedLines`, `renderings`, `BeginRendering`, `PlayRenderedLine` "played from a rendering, no generation wait");
  - 0.35 s of silence separates lines (`SeamSilenceSeconds`);
  - drawing strokes are paced to line timing (`LineGenerated`, `LineAudioQueued`, `OnLineAudioQueued`, `lineAudioOffsets`).
- **Observed:** Keep-warm: an idle teardown closes the socket only when the app is not busy (`ArmIdleTeardown`, lit "staying warm"), plus `KeepMouthWarm` and `hasOpenedASessionThisProcess`. Changing the voice or speed reopens the session (`RefreshAfterConfigChangeAsync`).
- **Observed:** When the mouth cannot open, the app answers in text (lit in `NarrateAloudAsync`) and reports region or network unavailability (`RealtimeProviderUnavailableReason+RegionUnsupported/NetworkBlocksProvider`, `RealtimeProviderAvailabilityDiagnosis`).
- **Observed:** Voices: cedar (default), marin, alloy, ash, ballad, coral, echo, sage, shimmer, verse. Each has a short tagline and a preview MP3. Speeds are 0.5x, 0.75x, 1x, 1.25x and 1.5x (`MouthConfig` VoiceIdentifier/Speed/UsageContext; settings `VoiceIdentifier`, `SpeechSpeed`).
- **Observed:** Playback goes through WASAPI at 24 kHz (`WasapiAudioOutput::VoiceSampleRate`=24000) with a 60 ms jitter pre-buffer and underrun refill (`VoiceRenderStream`). Played seconds and level are exposed for mouth and caption sync.
- **Observed:** Earcons are mixed at 48 kHz stereo with up to 8 voices (`EarconPlayer`). Separate XAudio2-based sound effects play for drawing marks (`Drawing.Audio.SoundPlayer`, Vortice.XAudio2).
- **Observed:** Captions show the spoken line as subtitles near the drawing (`LiveCaption`, `DrawingCaption`, settings `DrawingVoiceCaptions` and `DrawingSubtitles`).
- **Observed:** Latency instrumentation: `CompanionTurnMetrics` records TranscriptMs, CaptureMs, BrainFirstByteMs, FirstSayLineMs, FirstAudioMs, FirstSpokenMs and TotalMs. `BrainTurnTiming` and `OneBrainStatsPanel` show stats in a debug panel.
- **Observed:** Agent announcements: background agents can speak short progress or finish lines (setting `AgentAnnouncementsVoiceEnabled`, and cursor text announcements).

## 4. The "brain" turn

**Transport**
- **Observed:** `BrainClient` posts to `/brain/turn` and reads `text/event-stream`. Bodies over 8 KB are gzipped (`BrainRequestCompression`, header `x-clicky-accepts-gzip`). There are separate `/brain/compact` and `/brain/prewarm` calls.
- **Observed:** A debug dump directory can be set with the env var `HEYCLICKY_BRAIN_DUMP_DIR`.

**Turn body fields** (lit `CompanionSession::WriteTurnBody`, `TurnBodyParts`):
- thread, the user's words or typed text, an app "fact" (for app-initiated turns), a local time label;
- front app and process, the drawing currently on screen and its code;
- highlight gesture and text, page text that was read, live-state and plan notes;
- **screenshot** (JPEG base64 plus pixel size) and **other_screens**, attached images;
- walkthrough step, trigger/flow, session id, max rounds, async tools, turn token;
- flags: screenless, figure3d, takeover (computer use), `figures_float`, `library_items`, `routines_tool`, `saves_drawings`, `drive`;
- user layer and its signature, brain model, app reads.

**Reply stream** (`BrainReplyAccumulator`)
- **Observed:** Event fields include code, items, tool_calls, computer_calls, spoken sentence, usage, cost, model, done, error, message recorded/refunded, guided_max_rounds, keep_reasoning, search records, screenshot_file and user_layer.
- **Observed (key design):** The answer is a **small program**. `ProgramText::ExtractProgramCode` and `FirstSayLiteral`, together with `SayLines::Scan` looking for `say(` and `step`, show that one script interleaves **say("…") lines with drawing calls**. The app speaks the say lines and runs the drawing calls in the JS engine.
- **Inferred:** Line 1 can be read early from the streaming code, before the program finishes (lit "line 1 read early from the code").

**Screen capture**
- **Observed:** `ScreenCaptureService` has two backends: DXGI Desktop Duplication and Windows.Graphics.Capture, with Auto selection.
- **Observed:** It captures all displays (`CaptureAllAsync`, `CaptureAllAtDisplayPointSizeAsync`) or a region, with a JPEG encoder and quality set.
- **Observed:** Per turn the app sends the main screen plus `other_screens`, and knows the display under the cursor. The model can target a display (`onScreen(n)` helper).
- **Observed:** Capture happens when the turn begins (`CaptureForTurnAsync`, `CaptureSettleSeconds` option). After an action, the app waits for the screen to settle before capturing: it samples a 192 px grayscale image every 150 ms, needs 2 calm samples below 0.5 % change, and gives up at 4 s or after 1.5 s with no change (`ScreenSettle` consts).
- **Observed:** The app's own overlays are excluded from capture with `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`, falling back to `WDA_MONITOR` (`CaptureExclusion`). Setting `ShowInScreenRecordings` opts back in.
- **Observed:** Overlays hide themselves when Snipping Tool, ScreenClippingHost or ScreenSketch opens, using DWM cloak (`ScreenCaptureYield`).
- **Observed:** Exclusive-fullscreen games are detected and the model is told it cannot draw over them (`FullscreenGameNote`).

**Context management**
- **Observed:** `BrainSession` keeps a local thread per account:
  - up to 48 screenshots, trimmed to the newest 32 (`MaximumScreenshotsInThread`, `ScreenshotsKeptAfterTrim`), with older ones replaced by a tiny placeholder JPEG (`ShrinkOlderScreenshots`);
  - an idle timeout starts a new session (`IdleBeforeNewSession`), and recent sessions are restored after a restart (`RestoreSavedSessionIfRecentLocked`, `SaveToDisk`).
- **Observed:** Compaction runs when conversation tokens exceed 45 000 or input tokens exceed 75 000 (`BrainCompactionPolicy`). The server returns a drop/keep range and a note item (`/brain/compact`: drop_leading_items, keep_from_index, note_item, tokens_until_fold).
- **Observed:** Prompt-cache prewarm runs at the first key-down after a long idle, but only once the conversation exceeds about 40 000 tokens (`BrainPrewarmPolicy` const, `BrainSession::NoteKeyDown`, lit "cache warming at key-down").
- **Observed:** A settings row lets the user start a fresh session (`FreshSessionRow`).

**Tool loop**
- **Observed:** Up to 5 tool rounds per turn (`CompanionSession::MaximumToolRounds`=5). Some tools run in parallel and some async tools change the screen (`BrainToolCallPolicy` ParallelToolNames / ScreenChangingAsyncToolNames / ToolsHeldDuringTheTour). Tools can be held while the onboarding tour runs.
- **Observed:** Tool names the talk model can call. These were collected from `BrainToolCallPolicy::.cctor`, `CompanionSession::StatusLabelForTool`, `CompanionTools`, `LocalRootTools`, `WorkerTools`, `AgentTools` and `RealtimeMouth::.cctor` client capabilities:

  | Group | Tools |
  | --- | --- |
  | Client-side actions | `open_url`, `copy_to_clipboard`, `insert_text` (into the focused field; refuses secure, address-bar or own-window targets and falls back to clipboard), `capture_screen` |
  | Local files and shell | `shell_exec` (PowerShell `-NonInteractive -EncodedCommand`, 10 s default timeout, 4000-char output), `file_read` (≤4 MB), `file_write`, `file_list` (≤100) |
  | Worker / backend | `web_search`, `send_to_higher_model` (escalation, labelled "Thinking deeper"), `read_connected_apps`, `save_memory`, `clicky_memory`, `google_calendar_find_events`, `google_calendar_create_event`, `play_on_spotify` (`spotify_search`) |
  | Background agents | `sessions_spawn`, `sessions_send`, `session_status`, `sessions_interrupt`, `sessions_yield`, `sessions_answer_request`, `runtime_status`, `open_agent_dashboard`, `clicky_artifacts`, `open_artifact`, `reveal_artifact`, `show_result`, `routines` |
  | Drawing / onboarding | `save_drawing`, `tutorial_draw_on_screen`, `skip_onboarding_interview` |

- **Observed:** A user-visible status line is shown per tool (for example "Looking at your screen…", "Typing it in…").

**Computer use ("Drive" / take-over)**
- **Observed:** Separate from tools, the brain can return `computer_calls` (`BrainComputerCall` with CallId, Actions and PendingSafetyChecks).
- **Observed:** Action vocabulary includes click, double_click, triple_click, mouse_down/up, drag, scroll, type, keys/hold_key, move, wait, screenshot, zoom and cursor_position. Key-name normalisation maps Mac names (cmd, opt) to Windows (`ClickyDriveActions`).
- **Observed:** Consent flow:
  - the app offers to do the step, the user approves and the user watches (`ClickyTakeOverController` Idle/Offered/Driving, `TakeOverConsentCard`, `TakeOverOverlay`);
  - reply markers record allowed, declined or drove;
  - end reasons are Finished, UserTookOver, SafetyStop, CouldNotSee, OutOfAgentMessages and Failed (`AgentDriveEnding`).
- **Observed:** Execution goes through `ClickyScreenActuator`: SendInput with eased glides, holds and chords, keyboard-layout aware typing (`VkKeyScanExW`), and release of stuck keys after a drive.
- **Observed:** The user's input is held through low-level mouse and keyboard hooks, with Escape to abort, a heartbeat watchdog and a time cap (`ClickyUserInputBlocker`). Moving the mouse about 40 px or pressing a key takes control back (`ClickyTakeBackWatch`, `UserTakeOverDistance`=40).
- **Observed:** Elevated windows are detected through process integrity level (`ForegroundIntegrity`).
- **Observed:** Background jobs can ask the user to "let me do one step on your PC" (`ClickyDriveLines::AsksToUsePc`, `MacStepWaiters`).

## 5. On-screen companion (cursor buddy, pointing, drawing)

**Cursor buddy** (`Platform.Overlay.CursorOverlay` / `CursorLayer`, drawn with Direct2D/DirectComposition on one transparent overlay window per virtual screen, `OverlayHost` and `FramePacer`)
- **Observed:** The buddy is a small rounded triangle in a cursor colour. Colours are Blue #3380FF (default), Red #FF4C4C, Yellow #D4A017 and Green #26C281 (`CursorOverlay+Colors`).
- **Observed:** It follows the real cursor at an offset of (35, 25) pt and rests rotated −35°.
- **Observed:** Navigation modes (`CursorLayer+NavigationMode`): FollowingCursor, NavigatingToTarget, PointingAtTarget, RidingPen, ParkedAtPen.
- **Observed:** Fly-to uses springs and an eased flight path (`OverlayCursorSpring`, `OverlayCursorFlightPath`, `PointAtAsync`, `ReturnToCursor`). A return is cancelled if the user moves more than 100 pt.
- **Observed:** "Rides the pen": the buddy travels with the drawing pen along planned flights (`PenRideTrajectory`, minimum flight 40 pt / 0.12 s). It respects the Windows "reduce animations" setting (`SPI_GETCLIENTAREAANIMATION`).
- **Observed:** Voice-state visuals:
  - `VoiceState` Idle, Listening, Processing, Responding;
  - 5 waveform bars driven by the microphone level;
  - thinking dots looping on a 2.16 s cycle (`ThinkingDots`);
  - a dwell ring;
  - corner dots.
- **Observed:** Dock and undock into the notch (`DockAsync`, `UndockAsync`, setting `CursorDocked`).
- **Observed:** Shake to detach: a fast shake of the mouse detaches the buddy, which reattaches after 3 s. The shake threshold is about 6 reversals and 260 pt of path in 0.5 s (`CursorLayer` Shake* consts, setting `ShakeToDetachEnabled`).
- **Observed:** There is a welcome bubble and a "say goodbye and dock" animation (1.1 s hold, 0.6 s fade).
- **Observed:** Speech bubble next to the cursor (`CursorMessageBubble`): word-by-word reveal, then hold, then fade, at most 300 px wide.

**Click target ring and guided walkthroughs**
- **Observed:** `ClickTargetLayer` draws:
  - a dashed rounded outline around a control, with a soft glow and a faint fill;
  - a gel-style label pill above it;
  - a breathing pulse;
  - a spring reveal.
- **Observed:** Guided steps (`CompanionSession+GuidedTarget`: Label, CenterPx, ControlFramePx, RadiusPx, Step, IsHover, SpokenInstruction, Typed):
  - the model's box is snapped to the real UIA control under it (`SnappedTargetAsync`, "snapped to the control … from the model's box");
  - the radius is clamped to 28–180 pt;
  - the app watches for the user's click with low-level mouse hooks (`PointerInput+PressWatch`, `OnGuidedPress`). A click on target advances to the next step, re-captures the screen and runs another turn. A click elsewhere ends the guide and tells the model;
  - hover targets complete after resting 2 s;
  - walkthroughs are capped at 15 steps; armed targets expire after 120 s and the oldest after 600 s;
  - "is the next step a manual action" is detected from the instruction text with an action-verb regex (`StepInstructionPattern`);
  - the screen-change tolerance per step is 3 %;
  - the guide exits passively when another app comes to the front.
- **Inferred:** Steps "the app cannot see finish" use `waitForUser()` in the program.

**User highlight gesture (user draws on screen to point)**
- **Observed:** `HighlightOverlay` / `HighlightLayer` draw a highlighter ink trail that follows the mouse while the user talks: 8 pt wide, 55 % alpha, fading after 1 s, with dwell detection at 12 pt / 0.35 s (setting `ScreenHighlightEnabled`).
- **Observed:** `HighlightCompositor` composites the strokes onto the screenshot. It classifies each stroke as circled, line-from-to, line-across or scribbled, names the screen region and describes the gesture order in words for the model.
- **Observed:** `HighlightTextHarvester` reads UIA text and link URLs under the strokes and passes the exact strings.
- **Observed:** Page reads: `UiaPageText` reads the focused document's text over the UIA TextPattern with node and time caps, starting early at key-down (`PageReads` waits 0.8–1.6 s at turn time).

**Screen drawing** (`HeyClicky.Drawing`: `ScreenDrawingCanvas`, `ScreenPainter`, `NativeEngine` in V8, `DrawingThread`)
- **Observed:** The model writes a program. The engine turns it into hand-drawn SVG-like marks: tapered pen, wobble, watercolour wash, handwriting in Patrick Hand, highlighter, and a pen-reveal animation in drawing order. The marks are painted natively (Direct2D/Win2D, DirectComposition) with no browser.
- **Observed:** The drawing can be paced to speech (strokes after a `say()` line are spread across that line's audio). `finishNow` draws everything at once.
- **Observed:** Live edits on an existing drawing: recolor, restyle, move, moveTo, scale, erase (the pen runs backwards), redraw, add, highlight and undo. Every part has an id, a description and a box, and `state()` lists them for the model.
- **Observed:** Engine primitives (names only): `line`, `arrow`, `curve`, `poly`, `path`, `circle`, `rect`, `dot`, `image`, `text`, `highlight`, `graph` (axes, curves, marked points), `timeline`, `figure` (3D), `scene3d`, and the edit ops above.
- **Observed:** Screen-marking helpers in `clicky-draw-pieces.js` (names only):

  | Purpose | Helpers |
  | --- | --- |
  | Point at and name things | `point` (ring a spot plus a name pill), `target`/`hover` (control box), `frame`, `ring`, `trace` (outline an irregular region), `label`, `mark`, `tag`, `underline`, `strike`, `cross`, `check`, `star` |
  | Guidance | `scroll` (scroll-here chevrons), `spotlight` (dim everything except one box), `steps`/badges, `button` (a clickable on-screen button that sends an action back), `tie` (arrow from card row to photo) |
  | Paper explanations | `paper`/`card`, `sticky`, `receipt`, `clipping`, `photo` (image search by query), `heading`, `title`, `subtitle`, `legend` |
  | Actions as marks | `typeText(x, y, text)` (put text into the field at a point), `copyText`, `openPage(url)`, `handOff({task, …})` (give work to an agent), `sfx(name)`, `waitForUser()`, `onScreen(n)`, `plan()`, `lessonTitle()` |
  | Onboarding tour only | `showNotch`, `nextQuestions`, `tourJob`, `tuckJobs` |

- **Observed:** Marks on the real screen use the cursor colour with a glow, name pills and numbered badges. Paper-style cards are kept for explanations on empty screen space. The engine checks layout so labelled boxes do not pile onto each other (stated in the JS header comments).
- **Observed:** On-screen UI parts: `ActionButtons` (clickable buttons in the drawing), `PinnedCards` (cards can be pinned, dragged, expanded and tucked, and fly into the notch), `DrawingSuggestionCards`, `GrabCursors`, a stale-watch that clears the drawing when the underlying window moves (`ScreenDrawingStaleWatch`) and `CardHandles`.
- **Observed:** Lessons: a drawing plus its narration can be recorded to MP4 through Media Foundation (`LessonVideoRecorder`, `Mp4VideoWriter`, `Mp4Soundtrack`, `LessonNarration` WAV, at least 3 lines). They can be replayed in a drawing window with a beat scrubber and download (`ClickyDrawingWindowView+BeatScrubber`), and saved to a Lessons library (settings `SaveDrawings` and `KeepDrawingsInChat`).

**3D figures** (`HeyClicky.Drawing3D`)
- **Observed:** The 3D renderer is Direct3D 11 through Vortice with runtime HLSL compile (`Shaders`, `Pipelines`, `D3DCompiler`), plus an ink outline, a "studio" look, labels in 3D (`LabelWords`, `LabelPlacement3D`), camera motion and clicks on parts (`StageClick`).
- **Observed:** Geometry is built in a separate V8 context running three.js (`ClickyThreeKernel`, `clicky-three-parts.js`). Part kinds: `arrow`, `blob`, `box`, `capsule`, `cone`, `cyl`, `extrude`, `gear`, `lathe`, `pipe`, `poly`, `sphere`, `spring`, `surface`, `torus`, `tube`, with bevels, thickness and uneven stretch.
- **Observed:** Figures can float above the drawing (flag `figures_float`).

**Notch and other windows** (HeyClicky.dll `App.Notch.*`, `App.Shell.*`)
- **Observed:** The notch is a top-of-screen pill and panel showing listening, thinking, speaking, dictation and agent activity phases (`NotchActivityPhase`). It holds the voice pill, the text composer, a chat transcript with drawing rows and hand-off rows, a full-reply view, a document viewer and update cards.
- **Observed:** `NotchLiveEvent` covers about 30 kinds, including MicrophoneTrouble, DictationCopiedToClipboard, MeetingCountdown and SpotifyVoiceTip.
- **Observed:** The app also has a tray icon, a taskbar button, overlay and panel windows, and a dock of agent cards (`App.Dock`).
- **Observed:** Character portraits for named companions (`App.Portraits`):
  - 13 expressions (Curious, Listening, Thinking, Happy, Sleepy, Surprised, Skeptical, Shy, Excited, Laughing, Determined, Loving, Sad);
  - 6 reactions (Wave, Wink, Boop, Giggle, Celebrate, Dance);
  - 4 moods (Running, Happy, Sad, Sleepy);
  - hairstyle, skin tone, outfit and colourway parts.
- **Inferred:** These portraits are agent avatars, not the cursor buddy.

## 6. Memory, notes, clipboard, personal knowledge

- **Observed:** The brain has two memory tools: `save_memory` (worker) and `clicky_memory` (recall). The settings text says a session that ends still gets "its note and memory written" (`BrainSession::EndSessionOnWorker`).
- **Inferred:** Memory is mostly server-side. The server returns a `user_layer` plus a `user_layer_signature`, and the client caches both and sends them back on each turn and prewarm. This looks like a signed, server-built user profile or memory block.
- **Observed:** Obsidian: the user picks a local vault (auto-detected from `obsidian.json`; setting `ObsidianVaultPath`). Spawned agents get an Obsidian skill, and the vault appears under connected apps for the brain (`App.Settings.ObsidianVault`, `BrainContext::WithLocalApps`).
- **Observed:** Clipboard: `copy_to_clipboard` and `copyText` exist. Dictation and insert fall back to the clipboard. Clipboard save and restore happens around paste (`TextInsertion::SnapshotClipboard` / `RestoreClipboard`). No clipboard history or monitoring feature was found.
- **Observed:** Notes and live notes ("Ink"): `ClickyInkNoteStore`, `ClickyLiveNoteStore` (settings `LiveNotesEnabled`, `LiveNotesStreamPatches`), and `ClickyNotes` (condensed summaries and progress notes about jobs, fed back into the brain as "notes"). Job notes are markdown with custom card objects. This is mainly agent-side and out of this pass's scope.
- **Observed:** Local data: the dictation transcript archive (14 days), saved brain sessions, settings JSON (`SettingsStore`) and dictation vocabulary and learned words.

## 7. Native boundaries that indicate capabilities

Source: ImplMap rows (REA `ev_067d6326…` for Platform) plus COM types generated by CsWin32 in the `Windows.Win32.*` namespaces of Platform.

| Capability | Evidence |
| --- | --- |
| Global hotkeys and input observation | `SetWindowsHookExW` (WH_KEYBOARD_LL / WH_MOUSE_LL), `CallNextHookEx`, `RegisterRawInputDevices`, `GetRawInputData`, `GetAsyncKeyState`, `GetKeyState`, `GetKeyboardLayout(List)`, `MapVirtualKeyW`, `VkKeyScanExW` |
| Input injection | `SendInput` (Platform, Core, Contracts, HeyClicky), `SetCursorPos`, legacy `keybd_event` / `mouse_event` (HeyClicky.dll) |
| Foreground and window control | `SetForegroundWindow`, `AllowSetForegroundWindow`, `AttachThreadInput`, `LockSetForegroundWindow`, `BringWindowToTop`, `EnumWindows`, `WindowFromPoint`, `GetAncestor`, `SetWinEventHook` (foreground tracking), `PrintWindow` |
| UI Automation | `CUIAutomation8`, `IUIAutomation2`, TextPattern/TextRange, ValuePattern, focus/property/structure event handlers, `IAccessible` |
| Overlay and composition | `DCompositionCreateDevice2/3`, `DCompositionWaitForCompositorClock`, `DwmFlush`, `DwmGetCompositionTimingInfo`, `DwmSet/GetWindowAttribute` (cloak), `DwmExtendFrameIntoClientArea`, `DwmEnableBlurBehindWindow`, `SetLayeredWindowAttributes`, `SetWindowDisplayAffinity` (exclude from capture), `D3DKMTSetProcessSchedulingPriorityClass` (GPU priority), `avrt` MMCSS thread priority |
| Screen capture | `CreateDirect3D11DeviceFromDXGIDevice`, Windows.Graphics.Capture (`GraphicsCaptureItem`, `GraphicsCaptureAccess`), DXGI Desktop Duplication (Vortice) |
| Audio | WASAPI COM (`IAudioClient`, capture/render/clock clients, `IMMDeviceEnumerator`, `IMMNotificationClient`, `IAudioEndpointVolume(+Callback)`, `IAudioMeterInformation`), XAudio2 (Drawing) |
| Clipboard | `OpenClipboard`, `SetClipboardData`, `GetClipboardSequenceNumber`, `EnumClipboardFormats`, `RegisterClipboardFormatW`, `GetClipboardOwner` |
| Quiet, DND and presence | `SHQueryUserNotificationState`, `NtQueryWnfStateData` (quiet hours), `GetLastInputInfo` |
| Power | `CallNtPowerInformation`, `RegisterPowerSettingNotification` (HeyClicky.dll). Per the earlier pass, `PowerCreateRequest`/`PowerSetRequest` live in Agents. |
| Security and process | `OpenProcessToken`, `GetTokenInformation` (integrity level), `QueryFullProcessImageNameW`, `CheckRemoteDebuggerPresent`, `MiniDumpWriteDump` (crash) |
| Media | Media Foundation `MFCreateSinkWriterFromURL` / `SourceReader` (MP4 lessons) |

## Open unknowns

1. TimeSpan-typed tunables are not constants, so their values are unknown: session idle timeout, saved-thread retention, warm-ears window, mouth idle teardown, prewarm spacing, minimum hold, release tail. Reading them needs `.cctor` IL decoding of `TimeSpan.From*` calls.
2. Which tools run client-side and which run server-side is only partly known. `CompanionTools::ClientToolNames` covers open_url, copy_to_clipboard and insert_text; `LocalRootTools` covers shell and files. Whether the talk model can call `shell_exec` and `file_write` directly, or only agents can, needs `.cctor` and call-graph tracing.
3. Whether interruption by voice is possible at all. Everything points to press-to-interrupt only.
4. The exact server-side cleanup behaviour for dictation (punctuation, filler removal, rewrite) and the contents of the user layer are server logic and are not visible.
5. The brain models and system prompts are server-side. The client only lists selectable model ids (GPT-6.1 Sol variants, Claude 5.5 experimental, DeepSeek) and `send_to_higher_model` escalation.
6. The real screenshot resolution and JPEG quality per turn: `DisplayPointSizeMaxDimension` and `TargetSize` were not decoded.
7. How a computer-use `PendingSafetyChecks` item is shown to the user: no UI string ties to it beyond "safety_check" and "SafetyStop".
8. The default values for the Highlight, `StreamMode` and `OneBrainReadsApps` settings were not decoded.
