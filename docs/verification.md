# Windows preview verification

Local run: 11 October 2026 on Windows 11 x64. Working branch: `codex/windows-product`, based on `main` at `c84cef9`. The original Cursor checkout was preserved. This record distinguishes implementation, automated checks, and actual desktop observation.

## Passed

| Check | Actual evidence |
| --- | --- |
| Release solution build | Zero warnings and errors |
| Unit tests | 135 passed, zero failed or skipped after ChatGPT integration and final-message stream checks |
| ChatGPT protocol checks | Signed/forged/unsigned identity tokens, nonce/audience/issuer/expiry rejection, JWKS failure, exact loopback callback, account binding, rotating refresh/sign-out serialization, permission loss, ordered model catalog, image/history request format, and interrupted/failed stream rejection tested with isolated fixtures |
| ChatGPT connection screen | Human completed sign-in; Settings visibly showed Connected, plan usage enabled, and GPT-6.1-Sol; protected token file exists without reading its contents; connection survived app update |
| Live ChatGPT Ask | Fresh sandbox field contained `mint 47`; the completed GPT-6.1-Sol answer read it exactly, with HTTP 200 and `response.completed` receipts |
| Live POINT target | The returned mapped point `(1776,1014)` matched the actual sandbox Green button's accessibility center `(1776,1013)`; this run did not capture the visible tip throughout speech |
| Live ChatGPT Agent | Standalone sandbox, actual model calls, three separately reviewed production approvals; real button count became one and field equalled exact `hello`; model recaptured and reported the verified result |
| Live Agent decline | Declined a model-proposed extra click; count stayed one, field stayed `hello`, Home reported that the agent stopped |
| Updated live stream handling | Actual replies included two message items with one commentary item and an empty terminal output array; only the final plan was parsed. The updated app then clicked the field, typed exact `hello`, recaptured, and reported the verified count of one |
| Updated live Ask / Stop | A fresh completed answer correctly read `hello` and the click count; Stop cancelled its local spoken reply (`turn_cancelled`). Sandbox values remained unchanged. The answer completed before Stop, so this does not verify cancellation during pending inference |
| Full-app action demonstration | Three separate visible approvals; real sandbox green-button count became one and the field contained exactly `hello` |
| Full-app external Notepad | Typed Agent request, target selected during countdown, normal approval dialog, visible `hello` and five-character status; saved owned test file verified equal to exact `hello` |
| Published app decline | Declined the first approval; sandbox stayed Ready and its field remained empty; Home reported that the agent stopped |
| Dedicated external action harness | Its own launched process/window verified; exact `hello`, not a substring; three test-only automatic approvals |
| Two-monitor capture | Actual WGC JPEG capture, 1280 × 720 primary image, valid JPEG bytes; negative-origin secondary monitor supported |
| Overlay position | Actual tip window at virtual position `(-512,360)` and retained location during response; passed tolerance check |
| Published pointing demo | Visible pointer and response bubble at screen center; Home displayed Pointing, then returned to idle |
| Local Windows recognition | Installed English (United States) recognizer decoded generated WAV speech: `Hello there please open the window` |
| Real local TTS cancellation | Speech cancelled in 378 ms during the dedicated speech check |
| Secure storage | Small temporary Credential Manager record and 11,120-byte DPAPI-protected record round-tripped and were removed by the test |
| Display scaling | Home and Settings Connection/Voice tabs visibly inspected; fields and action buttons readable |
| Missing provider | Published app accepted typed input and displayed instructions to connect a provider; automated check confirms capture is skipped |
| Distribution | Self-contained EXE launched from its published path; unsigned MSIX created with real logos and license notices |

The built-in full-app demonstration used the offline planner and normal production approval dialogs. The external test executable used automatic approval only inside its own test process. The separate live GPT-6.1-Sol run used the standalone sandbox and reviewed each proposed action. That proves this bounded sandbox task, not general desktop reliability. The generated WAV test proves local speech integration, not physical microphone accuracy; two earlier Home transcripts were garbled, so voice accuracy remains unaccepted.

The separate Notepad check used the opt-in offline Agent fixture with the normal production confirmation/focus-restoration path. It opened an owned blank test file, selected that editor during Home's countdown, and approved a single `type_text: hello` action. The screenshot showed `hello` and five characters; saving the file confirmed its exact contents. Other Notepad tabs were preserved. This verifies the handover's specific external-editor input requirement without treating an input log as proof.

The updated live retest also observed a focus-restoration rejection before typing: the field remained empty and Home asked to reselect the destination. A fresh bounded request, with the sandbox selected and approvals reviewed, entered exact `hello` successfully. This is evidence that the guard stops input when restoration fails, and a limitation for broader focus reliability. Live HTTP receipts returned status 200 and `response.completed`; this route did not expose a request ID.

## Fixes motivated by observed failures

- Approval could lose the selected window/control and type into the wrong target. Focus is now restored and verified; failure stops the action instead of falling back to the desktop.
- A test could accidentally select another sandbox with the same title and accept `hellohello`. It now scopes to its own process and requires exact `hello`.
- The legacy offline Agent plan launched Run/Notepad and claimed completion without inspecting the editor. It now types only the test word into an already selected field and explicitly asks for visual verification.
- The low-level keyboard callback read modifier state before Windows updated it. It now tracks physical key events, ignores injected agent events, and reserves Escape for cancellation.
- Saved debug transcript fixtures could replace real voice input on later launches. Normal launches now ignore that override.
- UI scaling clipped navigation and settings. Layout construction now uses a consistent DPI baseline and completes scaling after controls are built.
- Idle overlay animation continuously repainted the desktop; the timer now runs only while the overlay is visible. The transparency key uses a neutral dark edge rather than a purple fringe.
- A live Agent run stopped with two concatenated JSON values. The Responses reader now uses completed output items, excludes commentary messages, and still waits for `response.completed`. This live route's terminal output array was empty, so finished items are retained from `response.output_item.done`. Regression fixtures cover both wire forms, the old concatenation error, and refusal/incomplete/commentary-only rejection. Logs record item counts, not content.

## Commands

```powershell
dotnet build OpenClicky.sln -c Release
dotnet test OpenClicky.sln -c Release --no-build
dotnet run --project apps/windows/OpenClicky.Capture.Smoke -c Release
dotnet run --project apps/windows/OpenClicky.Overlay.Smoke -c Release
dotnet run --project apps/windows/OpenClicky.Agent.Smoke -c Release -- apps/windows/OpenClicky.Sandbox/bin/Release/net8.0-windows/OpenClicky.Sandbox.exe
dotnet run --project apps/windows/OpenClicky.App.SecretStore.Smoke -c Release
dotnet run --project apps/windows/OpenClicky.App.SecretStore.Smoke -c Release -- --speech
powershell -ExecutionPolicy Bypass -File scripts/publish-exe.ps1
powershell -ExecutionPolicy Bypass -File scripts/pack-msix.ps1
```

Desktop test executables require an interactive desktop. Do not run several input/overlay checks concurrently: they share foreground focus and the real pointer.

To repeat the external-editor check without a provider, open a fresh harmless file in Notepad, launch OpenClicky with `OPENCLICKY_OFFLINE_AGENT=1` set only for that process, submit a typed Agent request, select the blank editor during the countdown, and review the approval. Inspect the actual text and save/read the owned file if exact verification is needed. Quit that fixture instance and relaunch normally afterward; do not leave offline flags or transcript fixtures active for daily use.

## Pending acceptance

1. **Physical voice accuracy.** Hold the default shortcut, speak a short request, release, and compare the recognized text with what was said. Earlier garbled transcripts require investigation before voice can be accepted. Repeat with a remapped shortcut; test Escape while listening and during speech.
2. **Live visual pointing.** Capture the visible tip on the intended control throughout speech, including a secondary-monitor target. The live model's target coordinates and the separate overlay position/hold checks passed; these are separate evidence.
3. **Broader Agent reliability.** Repeat harmless real-world tasks beyond the bounded sandbox, preserving per-action review. A successful sandbox task is not blanket approval for consequential desktop actions.
4. **Pending inference cancellation.** Stop cancelled a live answer's local speech, and declining an action stopped the Agent. The attempted network cancellation happened after the answer completed; cancellation during a pending live provider request remains unverified.
5. **Distribution acceptance.** Signed installation, clean-machine launch, MSIX installation, and upgrade behavior remain unverified. GitHub CI has not run the new local branch.

Full HeyClicky parity is outside this preview's implemented scope; the missing features are listed in [the research map](heyclicky-research.md). Do not mark the product complete or operationally accepted based only on this local record.

## 0.3 screen guidance — 11 October 2026

This extends the historical 0.2 record above. The final Release build has zero warnings/errors; the full suite passed **158 tests**, zero failures/skips. A subsequent constructor-order cleanup rebuilt successfully. No production automatic approval was added. Agent continues using its original capture path and main model.

| Check | Evidence / acceptance |
| --- | --- |
| Live transcript pipeline | Partial words appear while listening, then final text enters Ask on release; focused tests cover cancellation, bypassing recorded WAV transcription, and empty recognition falling back to visibly labeled screen help without Agent |
| Installed recognizer | Native Windows generated-WAV check passed both preview and final recognition; normal microphone release initially discarded the transcript because SAPI reported cancellation, and the corrected completion handler passed the fixture |
| Local speech | Generated sentence decoded as `Hello there please open the window`; live release fixture returned `Hello there`; real TTS cancellation completed in 359 ms. These are synthetic integration checks, not physical microphone accuracy |
| Live circle / rectangle / arrow | GPT-6-Luna completed at 20:16:35; the sandbox screenshot visibly showed the circle around Green button, rectangle around the empty Type target, and arrow to Clear field. Status remained Ready; no controls were changed |
| ChatGPT automatic guidance | At 20:25:21, fresh guidance identified the blank browser chat and visibly outlined its actual chat input; no input was submitted to ChatGPT |
| Claude automatic guidance | After the detail-image fix, at 20:40:16 the narrow Claude Code workspace was correctly described and its visible site-preview play button was outlined. Final normal-mode retest at 20:43:40 used WGC monitor images plus a 935 × 924 GDI window detail and visibly marked the same real control. No Claude task or preview was started |
| Official web help | Final request completed HTTP 200 with one `web_search_call` at 20:41:02. Home visibly contained guidance for Claude's preview panel and a plain HTTPS link to an existing official Claude Code guide. Search output remains model-generated; an existing source is not a guarantee that every claim or target is correct |
| Quick reply choice | Settings selected GPT-6-Luna for Ask while the main GPT-6.1-Sol model stayed unchanged. The saved JSON retained the quick model, and Home showed it after restart |
| Mark validation / app context | Tests reject overflow, invalid screens, out-of-bounds boxes, unknown shapes, zero-sized marks and excessive marks; negative-origin monitors, detail-image mapping, clipped active-window bounds, app-switch debounce/cooldown, changed-window discard and preservation of ordinary follow-up history passed |
| Stop / guidance off | Stop visibly unchecked the guide and returned Home to Ready with Stop disabled. Injected Escape did not establish physical shortcut acceptance; the real Escape shortcut remains a manual check |
| Packaging | Updated self-contained EXE launched from its published path; 0.3 portable ZIP and unsigned 0.3.0.0 MSIX created. Installation/signing/clean-machine acceptance is still pending |

The live investigations caught old-task history bias, unrelated source attachment, and confusion between small overlapping windows. Screen orientation/web help now exclude task history, public help instructions require a relevant source, the active window's bounds guide the model, and Ask supplies a legible detail of its visible screen region. Automatic guidance minimizes Home before capture. Both full-monitor WGC and compatibility GDI were exercised; the original automatic capture setting was restored afterward. No private conversation database was read or imported. The two full monitor images and the detail are sent to the selected provider; microphone audio and Windows speech synthesis stay local with ChatGPT.

Remaining acceptance: physical microphone/accent/noise accuracy, physical/remapped voice shortcut and Escape, same-window navigation/layout changes during a reply, and broader app/control reliability. Transcription previews are live, but the AI request begins on release or explicit submission. This is not continuous duplex speech or a general always-listening assistant. Freehand drawing and guided multi-step walkthroughs are next-slice work; see [the feature plan](feature-plan.md).

## 0.3.1 microphone selection and Whisper — 11 October 2026

The user reported that the 0.3 live words were inaccurate. Windows dictation therefore failed real-user acceptance despite its generated-WAV checks. The user selected **AI Wireless Microphone** from the four detected endpoints. This update addresses both explicit input routing and the older recognition engine; it does not establish that microphone routing alone caused the inaccurate words.

- Release build: zero warnings/errors. Full suite: 158 passed, zero failed/skipped. The microphone ID persists through the existing secret-stripping settings save test.
- Native endpoint selector was visibly inspected with MR720, built-in Intel microphone array, A4tech camera and AI Wireless Microphone. Wireless selection was saved through the real dialog, checked on disk, and visibly retained in Home after the final restart. Main and quick models remain GPT-6.1-Sol and GPT-6-Luna.
- The English base.en model downloaded once, with exact size **147,964,211 bytes** and SHA-256 **a03779c86df3323075f5e796cb2ce5029f00ec8869eee3fdfb897afe36c6d002**. Model integrity is checked before native loading.
- Whisper's live preview and final fixture both returned **Explain what is on my screen.** The combined preview/final fixture took about 5.8 seconds, including the initial 2.5-second preview interval. This is generated speech, not Ahmad's microphone accuracy.
- The public whisper.cpp JFK sample's human speech was transcribed correctly, including **what you can do for your country**, in about 1.5 seconds. This is a reference speaker and recording, not accent/noise acceptance for the selected device.
- Silence returned no words. Already-cancelled and in-flight inference were checked; native in-flight cancellation completed in about 1.4 seconds. A synchronous-disposal failure was caught and fixed by awaiting processor disposal before releasing its model.
- Native conversion of 48 kHz stereo float input to 16 kHz mono PCM passed. A missing explicit endpoint failed instead of using another microphone.
- The saved physical wireless microphone produced a usable in-memory snapshot and final capture: 21,806 snapshot bytes and 15,840 final samples in the short check. Audio was discarded and no transcript was produced or sent from this check. This proves capture, not the correctness of spoken words or the level meter under actual speech.
- A self-contained single-file speech harness passed the same recognition/silence/cancellation/resampling checks. Whisper's loader requires a usable native-library path; both publish scripts now extract bundled content so its assembly-relative lookup works. The final product EXE restarted and visibly showed the saved microphone, Whisper and version 0.3.1.

The user repeated the real spoken phrase with the updated app and confirmed **Accurate now** for preview and final words. This accepts the first phrase on the selected wireless microphone; longer speech, background noise and other accents/languages still need evaluation. Previews update every few seconds depending on CPU speed. This version uses an English-only CPU model and requests shorter than one minute. Windows compatibility recognition uses the selected microphone's captured WAV before model download, with words shown after Send. Speech audio stays in memory and local; the public model download sends no audio. Ask continues sending transcript and screen images to the selected provider.

OpenWhispr source was read at commit `5f9a88b0481a066c147bfd2c38ea1d9acae502ee`; its application and scripts were not run. Device selection, local-model setup and transcript feedback patterns informed the native implementation. Its UI, notes, dictation and meeting features are not bundled or claimed as built; see [the integration plan](openwhispr-integration.md).

## 0.4.0 companion workspace — 11 October 2026

This is a locally built and inspected expansion, not full repository integration or a public release. The worktree remains uncommitted on `codex/windows-product`, based on `fd62fb37927ffef2156442f9c151e99eeb64c85c`; earlier local changes were preserved. The older durable goal remains unfinished and could not be replaced by the new scope. Development continued; only account connections are paused by the user's instruction.

The exact Release solution build passed with **zero warnings/errors**. The full solution suite passed **211 tests**, zero failures/skips, after the substantive overlay focus fix. A later Notes row-layout-only adjustment rebuilt cleanly and was visibly inspected in the final published EXE. Focused checks cover access policy, task pause/cancellation/steering, bounded memory/notes/history persistence, selected-area capture and annotation mapping, and the OpenCode protocol. These checks do not establish general desktop autonomy.

| Check | Evidence / acceptance |
| --- | --- |
| Published workspace | `artifacts/exe/win-x64/OpenClicky.exe` launched with footer 0.4.0. Chat, Tasks, Memory, Notes and Connections were visibly inspected. Main model remains `gpt-6.1-sol`, quick model `gpt-6-luna`, guidance off, and access defaults to Approve each action. |
| Normal desktop execution | The built-in offline test used ordinary production confirmation dialogs. Each of three actions was reviewed. Actual destination controls showed **Green button clicked 1 time(s)** and exact **hello**. Tasks showed the completed run, chat narrated the steps, and Home returned to Ready with Stop disabled. This is offline pipeline proof, not a new live AI task acceptance. |
| Overlay focus regression | Before the fix, showing the processing/buddy overlay stole foreground focus and caused repeated context-change rejection. Managed WinForms `TopMost` was removed from passive overlay forms; native `SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE)` supplies their z-order. A harmless foreground fixture then stayed selected through Processing, Hide, PointAt and Responding. Multi-monitor geometry included virtual bounds `[-1536,0,4096,1440]` and a negative-origin target `(-512,360)`; placement was within eight pixels and trail length stayed bounded at eighteen. |
| Native companion visuals | The friendly circular buddy and fading trail were visibly observed during the reviewed click actions. No claim is made for arbitrary animated/rich visuals or physical Escape from the region picker. |
| Live selected-area request | A native drag selected only the harmless test note. The `gpt-6-luna` ChatGPT route completed HTTP 200 and `response.completed` at 23:49; request ID was unavailable. It read **OpenClicky workspace test passed.** correctly. A repeat at 23:50 visibly outlined the actual note text and displayed its reply beside the buddy. The model returned a rectangle while saying it had circled the text; correct target placement passed, exact requested shape did not. No desktop controls were changed by these requests. |
| Explicit local note | Typing **Take a note, OpenClicky workspace test passed.** into the shared chat saved the exact note to local `notes.json`. Selecting it in Notes visibly showed the matching title and body. The local interceptor runs before screen capture/provider invocation. CRUD/search/Markdown export have focused checks; native export and physical voice notes remain open. |
| Memory/personality | Native explicit profile/fact controls and the plain-text/provider-sharing disclosure were inspected. No user profile or facts were changed. Persistence/context limits are covered by tests; real personalization acceptance and automatic learning are not established. |
| OpenCode | An isolated, password-protected loopback OpenCode **1.17.18** fixture passed health, empty session creation, message/status reads and abort with all permissions denied. It sent no model prompt and performed no tools or account access. Its own process was stopped afterward. The production Connections tab exposes health checking only; unified task handoff remains unbuilt. |
| AG-UI | Task progress can export bounded AG-UI-style NDJSON. This is an export adapter, not a live AG-UI transport or CopilotKit/OpenIntelligentUI renderer. |
| Voice preservation | The saved AI Wireless Microphone ID was retained, but Windows reported it unavailable; only three other endpoints were active. The app did not silently substitute another microphone. The absent English Whisper model was restored through its integrity-checked download; its exact 147,964,211-byte size and the 0.3.1 SHA-256 above matched. No new physical voice acceptance is claimed. |
| Packaging | The final self-contained EXE, four-file portable ZIP and unsigned 0.4.0.0 MSIX were created. ZIP contents, embedded EXE hash and MSIX manifest version were checked. Signing, installation and clean-machine acceptance remain open. |

Task modes are Preview, Approve each action, Auto and Full access. Auto requires initial plan approval, confirms hotkeys and uses conservative English matching for sensitive steps; it can miss consequential actions. Full access permits input without step approvals after the user's warning acknowledgment. Neither is a security boundary, and neither was selected for the user during native checks. Test automatic-confirm helpers remain confined to dedicated harnesses. Cancellation cannot undo input already performed, and model-reported completion is not independently verified outside the dedicated sandbox checks.

Selected-area requests send only the chosen pixels and transcript/context to ChatGPT. Ordinary screen requests can send all connected monitors plus active-window detail. Local voice audio, notes and history stay on this PC; explicit memory profile/latest twenty facts are included in future AI requests. No Gmail/Composio/Nango accounts were connected or read. Continuous duplex voice, OpenWork runtime task handoff, rich diagram/3D rendering, schedules and broader HeyClicky parity remain unfinished; see [the current feature plan](feature-plan.md).

## 0.4.1 native demos and regression fixes — 12 October 2026

The [40-case results report](demo-test-results.md) records bounded native passes, partial checks, blocked physical-device cases and manual settings checks. The full automated suite passed 216 tests, zero failures/skips. After the final Home-only scrolling fix, the exact Release build passed with zero warnings/errors and the expanded Home/task-state smoke passed; unchanged core tests were not repeated.

Live GPT-6.1-Sol made one actual sandbox button click and typed exact `DEMO-20261011 live AI passed` through three normal production approvals. GPT-6-Luna read that text, visibly showed a pointer/circle/rectangle/arrow together, and placed the response below their bounds. A calibrated selected-area request visibly circled Green and boxed Type target. Home returned to the new reply without manual scrolling. Built-in Help visibly pointed to Clear field. A completed inspection task stayed completed when Stop cancelled its final narration and returned Ready. Microsoft lookup returned an independently checked relevant official source; native browser-link opening was not accepted.

The user requested keeping two DEMO notes, two DEMO memory facts and the DEMO-Buddy profile for review. They remain and will affect future explicit-memory context until edited. Earlier data and credentials were preserved. Wireless microphone was unavailable; no device substitution or physical voice acceptance is claimed. Preview/Auto/Full and guide-sharing settings were not enabled by the agent. General desktop autonomy, live OpenCode/OpenWork handoff, rich generated UI, account connections, continuous duplex speech and scheduling remain unfinished. Packaging proof and the exact artifact hashes are in the current results report and local receipt; signing, installation and clean-machine acceptance remain open.
