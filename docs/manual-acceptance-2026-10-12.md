# Manual acceptance — 12 October 2026

## Quick physical test card for 0.6

Use **AI Wireless Microphone** and **Approve each action**. Keep the disposable Sandbox contents; append test text rather than clearing it. The running candidate and its hash are recorded in the implementation ledger. Report failures as well as successes.

| Check | Exactly what to do | What to report |
| --- | --- | --- |
| Automatic insertion | Click **Dictate into app**, then click the Sandbox **Type target** field. Say “Please keep this as raw dictation,” pause, then “This is the second sentence” and pause. Click **Stop recording** afterward. | Both phrases must appear once, in order, in the actual target field, with earlier text intact. Preview words alone do not pass. |
| Reviewed fallback | With recording stopped, edit the preserved draft to “Reviewed dictation test.” Click **Insert into original app** once. | Only the edited draft is inserted by that click; the raw transcript is unchanged and no form is submitted. |
| One-click conversation | Start once, then ask 20 short screen questions: seven in Sandbox, seven in Calculator and six on a harmless Chrome page. Wait for each reply and automatic return to Listening. Switch apps without restarting the conversation. Click the companion to stop at the end. | Count correct turns out of 20, every retry or wrong-window answer, and approximate first-word/reply delays. |
| Stop and interruption | Stop once while Listening, once while Thinking and once while Speaking. Separately let a reply finish through normal speakers without speaking, then repeat while speaking over it. | Stop must not restart listening. Report self-echo, lost first words and whether your interruption became one complete turn. |
| Physical shortcuts | With the Sandbox frontmost and dictation closed, hold Win+Alt+Space, say “Explain the green button,” then release. Repeat ten times. Save a supported alternative in Settings and repeat ten times; restore your preference. | Each attempt starts once and finishes one reply without entering continuous listening. Record all missed/repeated attempts. |
| Device recovery | While listening, unplug the AI Wireless receiver. Reconnect it, then explicitly start again. | Visible unavailable-device error, no silent substitute microphone, saved selection retained, and successful restart. |

For each check, note **pass/fail, retries, target app and approximate delay**. Noise/speaker conditions, active-task voice steering, mixed DPI, tray reopening and optional Chrome extension setup still have detailed checks below. These checks do not require enabling Full access.

This checklist covers the physical Windows behaviors that automated fixtures cannot prove. **Passed, user-confirmed on 12 October:** AI Wireless Microphone input level moves; both consecutive screen questions work with accurate words and automatic listening rearm in published OpenClicky 0.5.0. The first attempt encountered a stopped development build; the successful repeat used the reopened published app. The sections below still cover additional unaccepted conditions such as interruption, noise, shortcuts and disconnects.

**Follow-up and repair:** the earlier 0.5 dictation preview initially opened without words, and the default shortcut needed 3–4 attempts before replying. After the repair, Ahmad confirmed “Both fields show the words” on the physical first-attempt dictation retest. He subsequently confirmed the screen-question shortcut worked on its first try on the white sandbox. These preserve those bounded 0.5 checks; they do not accept 0.6 automatic insertion. In 0.6, **Dictate into app** inserts finalized raw segments on pause while retaining the raw transcript and editable draft. Stop automatic dictation before using the preserved draft as the reviewed fallback.

**Startup and capture also passed:** the user previously reported “Yes, Home is visible” while the native helper had a foreground-process error. Computer Use has since recovered and accepted Home relaunch, the shared Tools capture command, normal Quit/relaunch, and the published dictation UI workflow. Physical tray double-click and the checks below remain separate.

## OpenClicky 0.6 phase-one acceptance — 12 October 2026

This section adds the 0.6 checks without replacing the 0.5 evidence above. Keep three evidence types separate:

- **User physical evidence:** the 0.5 two-turn screen conversation, first-attempt dictation words, first-attempt default shortcut, Home relaunch, and published dictation workflow described above. These results do not automatically accept the rebuilt 0.6 package.
- **Automated and native fixture evidence:** lifecycle tests cover continuous listening, Stop, stale-turn protection, conditional speech interruption, microphone failure, current-window capture, selected-region capture, and preservation of the first words spoken during an interruption. Fixtures prove the code path, not microphone acoustics or a physical shortcut chord.
- **Local capability probe:** the currently connected Intel microphone produced an in-memory 16 kHz/16-bit capture and Windows reported acoustic echo cancellation. No audio file was retained. This does not prove speech accuracy or accept another microphone.

The selected **AI Wireless Microphone was absent during the earlier blocked check and is now reconnected**. Its exact-device capture probe passed with 15,520 final PCM samples; a separate production Windows communications recorder probe reported AEC active and returned 16,320 samples at 16 kHz, mono, PCM16. Both discarded their in-memory audio. This proves capture and advertised AEC capability, not physical accuracy, echo suppression, interruption, noise, or disconnect recovery. OpenClicky retains the saved selection; it must show an unavailable-device error rather than silently use another microphone.

### 0.6 behavior to verify

- Automatic dictation inserts each finalized raw segment into the selected writable field when the user pauses. It continues listening until the user stops it. This automatic path does not wait for draft review. The same session preserves a reviewed or edited draft; after stopping automatic insertion, **Insert into original app** provides the explicit fallback. Record which part of the single flow was tested.
- Ordinary conversation and dictation continue to use the selected NAudio microphone path. During spoken replies, OpenClicky starts a separate Windows communications capture only when local Whisper is available and dictation is inactive. It interrupts speech only after Windows reports AEC on that capture. If AEC is unavailable, the interruption listener is discarded and ordinary capture must keep working.
- **Superseded by the 2026-10-24 change (sub-project B, capture):** a normal screen request now captures every connected display (up to four, the cursor's display first) with OpenClicky's own windows excluded; the older statement follows for history. Previously: a normal screen request captured only the current foreground window. **Select screen area** captures only the chosen pixels (unchanged). It did not send all monitors. The current Home → Tools diagnostic also captures only the foreground window and checks exclusions first; its older all-monitor diagnostic evidence is historical.
- The overlay accepts seven mark types: rectangle, circle, arrow, polygon, freehand polyline, cubic curve and text. Automated parser/mapping and native fixture checks support all seven; a real model-produced curve and physical mixed-DPI alignment remain open.
- Phase one uses one companion across dock, voice, workspace, tasks, memory and notes. The workspace contains the actual CopilotKit v2 renderer. Synthetic transport/reload passed visibly, and a real ChatGPT chart rendered the exact requested 12/20/8 values; arbitrary generated UI and separate named companions are not accepted.

### Required 0.6 physical run

Record app version, microphone, provider/model, target app, shortcut, first-preview delay, endpoint delay, first spoken-audio delay, and pass/fail notes.

1. Complete **20 continuous conversation turns across three apps**, with at least five turns in each app. Check recognized words, correct current-window targeting, reply completion, rearm, and Stop. Record every retry, missed endpoint, stale-window response, or unintended restart; do not report only successful turns.
2. Complete **10 one-shot turns with the default shortcut** and **10 with a saved remapped shortcut**. Each turn must start listening once, submit once on release, and avoid entering continuous conversation. Restore the preferred mapping afterward.
3. Run a normal-speaker echo check. Let OpenClicky speak at the usual volume without user speech; its reply must not submit itself. Then speak over the reply. Mark interruption passed only if Windows reports AEC for the exact selected microphone, TTS stops promptly, and the complete user utterance including its first words becomes the next turn.
4. Repeat the speaker check with steady room noise and with a short non-speech sound. Neither may create a user turn or repeatedly stop speech. Record volume, approximate distance, and whether headphones or speakers were used.
5. While listening, disconnect the exact selected microphone. A visible error must stop the session without retrying or switching devices. Reconnect it and explicitly restart; the saved selection must remain unchanged.
6. During dictation, speak two phrases separated by a pause. Verify each finalized raw phrase is inserted automatically into the originally selected field, in order, with no Send or Enter action. Separately edit a preserved draft and use **Insert into original app**; confirm the raw transcript remains unchanged and the edited text is inserted only after that explicit click.
7. Ask for marks that exercise all seven supported shapes, including a cubic curve. Confirm each mark appears on the intended current window and expires after the display interval or window change. Record mixed-DPI/monitor placement separately.
8. In the one workspace, submit a chart request through the CopilotKit chat renderer and verify the visible values and labels against the request. Confirm the same companion identity/history appears after moving between Chat, Tasks, Memory and Notes; do not treat that as named-agent isolation.

### 0.6 acceptance record

- [x] Published 0.6 package visibly opened and version recorded: `phase-one-ready`, version 0.6.0.0, native embedded chat/composer and saved chart/diagram/calculator/cube inspected after restart. Calculator edits 7/5/2 and total 14, cube rotation 60°, and chart 12/40/8 were retained.
- [ ] 20/20 continuous turns across three apps.
- [ ] 10/10 default-shortcut turns.
- [ ] 10/10 remapped-shortcut turns.
- [ ] Speaker self-echo check.
- [ ] Verified-AEC spoken interruption check on the exact selected microphone.
- [ ] Room-noise and non-speech rejection checks.
- [ ] Disconnect, no-fallback, reconnect, and saved-selection check.
- [ ] Automatic raw dictation-on-pause check.
- [ ] Same-session reviewed-edit fallback check.
- [ ] Seven-shape overlay check, including a model-produced curve.
- [ ] Actual renderer result and single-companion continuity check.

As of 12 October 2026, the unchecked 0.6 physical boxes remain open. Synthetic/native checks and the exact AI Wireless capture/AEC capability probes are supporting evidence only.

## Prerequisites

- For the 0.6 section, use the running `artifacts\phase-one-ready\win-x64\OpenClicky.exe`; if another preview is open, close it normally first. Record the displayed version and the candidate receipt in the implementation ledger. Keep the retained `artifacts\exe\win-x64\OpenClicky.exe` only for reproducing the 0.5 evidence above; older ZIP/MSIX files do not prove 0.6 behavior.
- In **Settings → Voice & shortcut**, confirm **AI Wireless Microphone** is still selected and its input level moves while speaking.
- Confirm the accurate local Whisper model says it is ready. Hands-free conversation requires this model; compatibility or cloud transcription remains available only for one-shot hold-to-talk.
- Keep **Confirm each** as the task access default. Do not enable Full access for this test.
- Voice recognition and spoken replies are local by default. ChatGPT receives recognized text and screen images for a request, not raw microphone audio. OpenRouter receives recorded audio only if the user explicitly selects that cloud-transcription path and turns local recognition off.

## Start, speak, reply, and rearm

1. Left-click the companion dock once. Do not hold the button.
2. Confirm the dock and Home show **Listening** and the control changes to **Stop conversation**.
3. Say a clear screen question, then stop speaking normally.
4. Confirm recognized words appear while speaking. Record the first-preview delay; a cold first Whisper inference may take several seconds even though preview checks now begin after about one second.
5. After roughly 900 ms of silence, confirm the app changes to **Thinking** without another click or key release.
6. Confirm the response is a real configured-model reply about the current screen, not the offline pointing demo or a canned fixture.
7. Confirm the visible state says **Speaking** while Windows reads the response.
8. Confirm the app returns to **Listening** only after speech finishes.
9. Ask a second question without touching the mouse or keyboard. Confirm it completes as another turn.
10. Click **Stop conversation** while listening. Confirm the microphone stops, the state returns to ready, and silence does not restart it.

Repeat Stop once during Thinking and once during Speaking. Each must cancel the active work and must not automatically rearm.

## Automatic dictation and reviewed fallback check

The earlier published workflow passed target selection, local recording Start/Stop, exact insertion of a reviewed draft, and composer restoration. The deterministic native fixture also verified separate raw/draft fields and refusal after the destination closed. The repaired 0.5 build had user-confirmed first-attempt words in both fields. In 0.6 this is one dictation flow: automatic insertion happens on pause, while the raw transcript and editable draft remain available as the reviewed fallback. The sequence below is still required for physical 0.6 acceptance:

1. Keep **AI Wireless Microphone** connected. Use the disposable OpenClicky Agent Sandbox text field.
2. Click **Dictate into app**, then click **Type target** when the companion asks. It waits up to 20 seconds for a supported field; the preview must name the Sandbox destination.
3. Automatic dictation starts after the destination is selected. Say “Please keep this as raw dictation,” then pause. Do not hold any key.
4. While speaking, confirm words appear in the raw transcript and editable draft (the first local preview can take a few seconds). After the pause, confirm the finalized raw segment was inserted into the selected field automatically and listening resumed. Then say “This is the second sentence” and pause again without clicking. Inspect the actual Sandbox field: both segments must appear once, in order, with its prior text preserved. This automatic path inserts the recognized segment; it does not wait for draft edits or send/submit anything.
5. Stop automatic dictation. In the draft already preserved by that same session, make a small edit and confirm the raw words remain unchanged. Click **Insert into original app** and confirm the edited draft is inserted only after this explicit click. Do not describe this as a second dictation mode.
6. Return to Home and choose **Cancel** to leave the preview. The normal request composer should be visible again. No recording should remain active.

This mode performs no AI request or screenshot sharing. It deliberately refuses controls without a writable accessibility text value; optional AI rewriting is not built yet.

## Guided walkthrough voice check

The 0.6 guide's mouse controls now pass a real three-step Sandbox sequence: premature Next waits, Back reviews without undoing, Recheck works after a window move, appended text preserves existing lines, and the guide verifies the final click count before showing Complete. Its physical voice commands remain a separate check. Type a small goal in Home, click **Guide me step by step**, and bring the target app forward. Wait for the first instruction. Start conversation by left-clicking the companion once. Perform the indicated action, then say **next step** and pause. The guide should inspect the result before advancing. Say **previous step** to review without undoing, **recheck step** to take a fresh look, and **stop walkthrough** to close it and stop listening. An ordinary screen question should stay advisory; an action request should ask you to stop the guide first. Please use the disposable Sandbox for this physical test. Only the current foreground window, or an explicitly selected region, goes to the selected screen-help provider on each step request; audio recognition remains local.

Continuous voice no longer waits for the full eight-second annotation display after speech finishes. This removes an artificial delay; it does not guarantee instant local transcription or model replies. Capture, model and local-recognition finishing now have separate timing entries for further diagnosis.

## Shortcut and dock controls

1. First choose **Cancel** to leave the dictation preview and stop any ongoing conversation. Hold the configured shortcut, speak one request, and release it. While the dictation preview is active, shortcut speech belongs to that local draft instead of a screen question.
2. Confirm this remains a single push-to-talk turn and does not enter an ongoing session.
3. Left-click the dock while another app is focused. Confirm it toggles conversation without bringing OpenClicky Home to the foreground.
4. Drag the circular dock control. Confirm dragging moves it and does not toggle conversation.
5. Right-click the dock and verify **Open workspace**, **Select screen area**, **Clipboard shelf**, and **Stop conversation**.
6. Choose **Select screen area** from the right-click menu and complete a region request. Confirm only the selected pixels are shared.
7. Confirm a normal left-click starts or stops voice; it must not begin area selection.
8. Open **Clipboard shelf** from both Home and the dock menu. Confirm both routes reuse one shelf window.

## Task interruption and speech echo

1. Start an Agent task in **Confirm each** mode.
2. During execution, start conversation and say `pause task`. Confirm the task pauses at its next action boundary.
3. Say a short `change task: ...` instruction and confirm it is recorded as steering rather than executed as an unrelated screen question.
4. Say `resume task` and confirm execution resumes.
5. When the Agent reaches a terminal result, confirm session listening stops before the final spoken result and rearms only after that speech finishes.
6. Play the spoken reply through the normal speakers. Confirm OpenClicky does not submit its own reply as a new utterance.
7. Speak over a running Agent step and confirm the explicit voice request can pause or steer it. During TTS, interruption is available only when the dedicated Windows communications capture reports AEC for the selected microphone; otherwise wait for speech to finish before speaking.

## Errors, privacy, and switching apps

1. Start conversation, then disconnect or disable the selected microphone. Confirm a visible microphone error appears, conversation turns off, and no retry loop starts.
2. Reconnect the device and explicitly start conversation again. Confirm it recovers without changing the saved device.
3. Begin speaking over one app, switch to another before the silence endpoint, and observe the request. Confirm only the foreground screen selected when processing starts is used.
4. Switch windows after capture while a response is being prepared. Confirm stale marks and the old-window answer are discarded.
5. Confirm conversation never starts automatically at app launch or restart.
6. Confirm **Stop conversation**, **Stop**, or **Quit** ends microphone capture. Closing Home keeps the tray companion and an explicitly started conversation running; it is not a microphone-off action. Switching apps should discard stale screen guidance and rearm the same explicitly started session.
7. Confirm the task access setting remains **Confirm each** after restart and no session action enables Full access.

## Acceptance record

### Remaining tray, shortcut, and access checks

Run these only after the two-turn voice check. Use the disposable **OpenClicky Agent Sandbox**, not an email composer or important document.

1. **Reopen:** stop conversation, close Home with X, then double-click the OpenClicky tray icon. Check **^ Show hidden icons** near the clock if it is not on the taskbar; Ahmad confirmed the icon was there on 12 October. Home should return. Repeat using right-click on the small companion → Open workspace. In the updated build, launching OpenClicky.exe again should also reopen the same Home, with no duplicate instance or “already running” dialog.
2. **0.6 capture — passed through Home → Tools:** Computer Use inspected the candidate's current-window diagnostic: 158193 bytes, 1280×919, JPEG magic OK True, GDI fallback, WGC supported False. The older 0.5 result (133203 bytes, 1280×720, WGC, two monitors) remains historical. The tray route remains an optional discovery check. This diagnostic is different from asking the model about a screen.
3. **Physical shortcut:** Cancel out of dictation first. In Settings → Voice & shortcut, leave or save Win+Alt+Space. With the sandbox in front, hold those keys, wait until Listening appears, say “Explain the green button,” then release. One request should finish on the first attempt; it must not start an ongoing conversation. To check remapping, choose Ctrl+Shift+F9, Save, physically test that combination, then Reset and Save if you want the default back.
4. **Access warnings:** inspect the task-access dropdown. Preview should propose without performing input. Auto and Full access should show a warning before saving that choice; Cancel should retain the previous mode. Checking a warning is not evidence of elevated-mode execution. Keep Approve each action for the remaining tests.
5. **Quit/relaunch — now passed through Home → Tools:** Quit ended the app; relaunch showed Ready with AI Wireless Microphone, local Whisper and Approve each action retained. No microphone session started. The tray shares this exact Quit handler.

The [installed Computer Use skill](C:/Users/ahmad/.codex/plugins/cache/openai-bundled/computer-use/26.1007.21434/skills/computer-use/SKILL.md) requires its [guidance](C:/Users/ahmad/.codex/plugins/cache/openai-bundled/computer-use/26.1007.21434/docs/guidance.md), which says: “Do not use the Windows key or shortcuts involving the Windows key.” It also prohibits changing in-app security/privacy settings. Those checks require the user; injected input would not prove the physical microphone or keyboard behavior anyway.

Record the app version, selected microphone name, configured shortcut, local-model status, provider/model used for the real reply, and pass/fail notes for each section. A moving input meter proves device capture only; it does not accept transcription accuracy, endpoint timing, echo avoidance, screen targeting, or Agent steering.
