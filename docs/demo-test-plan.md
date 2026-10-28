# OpenClicky 0.4.1 native demo checklist

Prepared from the current `D:\OpenClicky` source on 12 October 2026. This is a repeatable checklist for sequential, visible Windows UI testing. Execution status, evidence, defects, fixes, and retests belong in the separate [demo test results report](demo-test-results.md); the presence of a checklist item does not imply that it passed.

## Demo rules

- Use the current 0.4.1 executable selected by the root task and record its exact path and file version before starting.
- Keep the visible desktop free of private material. Provider-backed screen requests can send visible pixels, the typed or spoken request, and explicit companion memory to the selected provider.
- Do not sign in, sign out, enter an API key, inspect stored credentials, connect Gmail, or change any account authorization during this pass.
- Do not select or enable **Full access**. Its visible option and warning copy are inventoried, but autonomous Full-access behavior is outside this run.
- Use **Approve each action** for every desktop-action demo. Approve only the built-in OpenClicky sandbox actions named below.
- **Cleanup:** Records with the prefix `DEMO-20261011` are review evidence. The user explicitly requested that the DEMO notes, companion profile, and facts remain available for review, so do not delete, clear, reset, or overwrite them.
- Computer use must not change security or privacy settings. Do not activate Preview, Auto, or Full access during the automated UI run. Elevated-mode behavior requires backend evidence plus a user-run manual acceptance step.
- When a destructive control needs coverage, open its confirmation and choose **No**. Do not treat retained DEMO review records as cleanup targets.
- Run only one desktop/overlay case at a time. These cases share the pointer, foreground focus, global shortcut, microphone, and overlay.
- Record visible proof from the destination control. A status label, log line, or model statement alone does not prove a click, keystroke, export, or screen mark succeeded.

Record these audit fields for each case in the [results report](demo-test-results.md):

| Field | Required result detail |
| --- | --- |
| Status | `Pass`, `Partial`, `Fail`, `Blocked`, `Manual required`, `Skipped by instruction`, or `Not built` |
| Actual result | What visibly happened |
| Evidence | Screenshot, recording, exported receipt, or exact backend output |
| Defect / blocker | Reproducible failure or missing prerequisite |
| Retest | Fix/worktree state and the later result |

Preserve the first failure evidence before applying a fix.

## A. Native shell, Home, and tray

### 1. Launch and native workspace inventory

**Result:** See [demo test results](demo-test-results.md).

1. Launch the selected 0.4.1 executable normally.
2. Do not scroll or inspect any previously saved conversation text.
3. Verify the Home window title, left navigation, footer version, provider/microphone status, Stop button, request composer, and workspace tabs.
4. Open each tab once: **Chat**, **Tasks**, **Memory**, **Notes**, and **Connections**.

**Pass criteria:** One native WinForms window opens without a blank surface or unhandled error. The footer derives `v0.4.1 · Windows` from `Application.ProductVersion`; all five tabs and the request composer remain usable at the minimum supported window size. The access selector defaults to **Approve each action** on a clean/default configuration. No integration is shown as connected merely because its tab exists.

### 2. Welcome chips and Ask/Agent composer behavior

**Result:** See [demo test results](demo-test-results.md).

1. On Chat, select each welcome chip without sending it.
2. Verify `What's on my screen?` and `Where is the save button?` select **Ask about my screen**.
3. Verify `Type a draft in the open app` selects **Do a task · Agent**.
4. Enter two lines with `Shift+Enter`, then remove them and leave the composer unsent.

**Pass criteria:** A chip copies its exact text into the composer and chooses the expected mode. `Shift+Enter` inserts a newline. No empty request starts. Switching modes does not start a request by itself.

### 3. Access selector inventory without changing modes

**Result:** See [demo test results](demo-test-results.md).

1. Confirm the selector currently shows **Approve each action**.
2. Open the list without selecting another value and confirm it lists **Preview only**, **Approve each action**, **Auto · approve task**, and **Full access**.
3. Close the list with Escape.
4. Do not activate Preview, Auto, or Full access through computer use.

**Pass criteria:** All four options are visible, the selected value remains Approve each action, and opening/closing the list starts no task. Auto and Full behavior are accepted only through focused backend policy tests and a later user-run manual check of their warnings and real behavior.

### 4. Tray lifecycle and Home close behavior

**Result:** See [demo test results](demo-test-results.md).

1. Close the Home window with its title-bar Close button.
2. Verify the OpenClicky tray icon remains present.
3. Double-click the tray icon; close Home again.
4. Right-click the tray icon and choose **Open OpenClicky**.
5. Choose **Settings…**, close Settings, and return to Home.

**Pass criteria:** Closing Home keeps the process in the tray. Both double-click and the tray command restore and activate the same Home window. Settings opens as the native Settings window. No duplicate Home window or tray icon appears.

### 5. Tray capture diagnostic

**Result:** See [demo test results](demo-test-results.md).

1. Put only harmless content on every connected monitor.
2. From the tray menu choose **Test capture once**.
3. Record the displayed dimensions, backend name, byte count, and JPEG-magic result without saving or opening the captured pixels.

**Pass criteria:** A bounded result dialog appears, reports positive dimensions and byte count, and says JPEG magic is true. Failure is acceptable only as a recorded Fail with its visible error; a success dialog does not prove provider submission.

### 6. Stop entry points while listening

**Result:** See [demo test results](demo-test-results.md).

1. Start **Listen to my voice** but do not speak private content.
2. Choose **Stop · Esc** in Home and verify the listening overlay closes.
3. Start listening again, press physical Escape, and verify the same result.
4. Start listening once more and use tray **Stop current task**.

**Pass criteria:** Each stop path returns Home to `Ready when you are`, clears the live transcript, hides the overlay, and does not submit a request. Stop remains harmless when no work is running.

### 7. Quit lifecycle

**Result:** See [demo test results](demo-test-results.md). **Run order:** last.

1. Finish all other cases and ensure no request/task is running.
2. Choose tray **Quit**.

**Pass criteria:** Home, Settings, overlays, and the tray icon close; no OpenClicky process from this demo remains. This case is last because it ends the session.

## B. Settings without account or secret changes

### 8. Settings layout and current provider panel

**Result:** See [demo test results](demo-test-results.md).

1. Open Settings and visit **Connection**, **Voice & shortcut**, **Privacy**, and **Advanced**.
2. Record the initially selected provider without exposing account email or key material in evidence.
3. Inspect only the currently selected provider panel. Do not toggle the provider radio buttons.
4. Do not click Continue with ChatGPT, Sign out, Refresh models, or Manage usage. Do not type an API key.

**Pass criteria:** All four tabs render without clipping. The current provider panel shows its expected controls without revealing a stored secret. The alternate provider panel remains source-inventoried but is not accepted through computer use because switching providers changes a security/account setting.

### 9. Microphone inventory and level meter

**Result:** See [demo test results](demo-test-results.md).

1. On **Voice & shortcut**, record the currently displayed microphone name only.
2. Click **Refresh microphones**.
3. Keep the original device selected and click **Test microphone**.
4. Speak the fixed phrase `OpenClicky demo microphone check` and watch the level meter.
5. Click the test control again or close Settings to stop testing.

**Pass criteria:** Refresh leaves a usable list or an explicit unavailable-device state. The selected device remains selected. The meter visibly moves during speech and stops after the test ends. Test audio is not played back, saved, or submitted to a provider.

### 10. Local speech/Whisper state and spoken-reply controls

**Result:** See [demo test results](demo-test-results.md).

1. Observe whether Settings reports Whisper ready or compatibility voice active.
2. Verify the **Download accurate voice · 142 MB**, **Use local speech recognition**, transcription-model, and **Read replies aloud using a Windows voice** controls are visible in the appropriate provider state.
3. Do not download a model in this pass. Do not change the saved switches.

**Pass criteria:** The status honestly matches the installed local model state. ChatGPT-plan selection requires local speech and disables cloud-transcription controls; OpenRouter exposes the configured transcription option. Merely viewing the screen performs no download or provider call.

### 11. Shortcut editor with restore

**Result:** See [demo test results](demo-test-results.md).

1. Record the current shortcut label.
2. Click the shortcut field and press a harmless unused combination with a modifier.
3. Verify the field shows the candidate and no request starts.
4. Click **Reset to Win+Alt+Space** and verify the default text appears.
5. Restore the exact original shortcut before clicking **Save changes**. Reopen Settings and verify the original value persisted.

**Pass criteria:** Bare keys are rejected with a visible hint; a valid modified chord is accepted as a draft. Save updates the Home/tray shortcut text. The original shortcut is restored at the end and works in the physical voice case.

### 12. Privacy controls and non-destructive clear confirmation

**Result:** See [demo test results](demo-test-results.md).

1. On Privacy, verify the history checkbox, clear-history button, screen-sharing disclosure, and extra protected-window-title field are visible.
2. Do not toggle history or edit protected titles.
3. Click **Clear conversation and task history** and choose **No**.

**Pass criteria:** The confirmation states that it covers saved conversations and task history while retaining Notes and explicit Memory, and defaults safely to No. Cancelling leaves the current chat/task view unchanged. No existing history is read, cleared, or exported.

### 13. Advanced diagnostics and settings close/save behavior

**Result:** See [demo test results](demo-test-results.md).

1. On Advanced, verify the localhost-proxy switch/address, GDI compatibility switch, offline fixture transcript field, diagnostic box, and **Refresh diagnostics** button.
2. Click **Refresh diagnostics** but do not copy, search, or publish its contents; keep screenshots cropped to exclude the log text.
3. Close Settings without editing. Reopen it, click **Save changes** with no changes, and verify Home returns to its idle state.

**Pass criteria:** Refresh does not freeze the window. No secret value is exposed in the visible controls. Saving unchanged settings does not change the provider, device, capture backend, shortcut, history preference, or protected-title list.

## C. Offline overlays and desktop actions

### 14. Offline pointing demo, buddy, target ring, trail, and optional TTS

**Result:** See [demo test results](demo-test-results.md).

1. Put Home over harmless desktop content.
2. Click **Try pointing demo**.
3. Do not move the pointer during the fly-to.
4. Observe the processing buddy, movement trail, final target ring/label, response bubble, and spoken reply if spoken replies were already enabled.

**Pass criteria:** Home labels this an offline pointing demo with no model call. The passive overlay appears without taking keyboard focus, moves toward the center, leaves a bounded fading trail, and visibly marks the target. It later hides and Home returns to Ready. TTS, when enabled, speaks locally and Stop interrupts it.

### 15. Built-in sandbox action demo in Approve-each mode

**Result:** See [demo test results](demo-test-results.md).

1. Confirm **Approve each action** is selected.
2. Click **Test desktop actions**.
3. In the OpenClicky-owned sandbox, review each confirmation. Approve only: click the green test button, focus the test field, and type `hello`.
4. After each approval, inspect the actual sandbox control before continuing.
5. Let the demo finish and return to Home.

**Pass criteria:** The sandbox reports the green button clicked exactly once and the field contains exactly `hello`. Three distinct actions are reviewed; passive overlays do not steal foreground focus. The task summary does not claim success until the sandbox controls show it. No external application is changed.

### 16. Confirmation decline and focus guard

**Result:** See [demo test results](demo-test-results.md).

1. Start the built-in sandbox action demo again.
2. Decline the first proposed action.
3. Verify no sandbox control changes and the run stops or records the rejection.
4. If the current harness exposes a harmless focus-change step, deliberately select Home before approval and verify input is rejected rather than redirected. Do not improvise this against another app.

**Pass criteria:** Decline performs no click or typing. A lost/changed destination produces a visible rejection and never falls back to typing on the desktop. Mark the focus portion Blocked if the sandbox cannot reproduce it safely through the UI.

### 17. Region selector cancellation

**Result:** See [demo test results](demo-test-results.md).

1. Put harmless content on all monitors.
2. Click **Select screen area**.
3. Verify the translucent virtual-desktop selector, crosshair, instructions, and live rectangle appear.
4. Press Escape without completing a region.

**Pass criteria:** Escape closes the selector, restores Home, sends no provider request, and leaves no overlay behind. A drag smaller than 8×8 pixels does not complete a selection.

### 18. Automatic app-switch guide — user-manual acceptance required

**Status: Skipped by instruction**

Computer use does not enable this screen-sharing/privacy setting. Source and focused tests can cover debounce, cooldown, and cancellation logic. A later user-run manual acceptance may enable the guide, switch to a harmless blank window, and press Escape while guidance is active.

**Manual pass criteria:** The checkbox clearly discloses screen sharing. App switch can trigger guidance only while idle. Escape turns guidance off, cancels the request/overlay, and returns to Ready. No target control is activated. Backend evidence alone does not establish native UX acceptance.

## D. Notes and explicit Memory

### 19. Notes create, select, update, and search

**Result:** See [demo test results](demo-test-results.md).

1. Open Notes and create title `DEMO-20261011 note` with body `Harmless native note for the 0.4 demo.`
2. Select the new row and verify the editor matches exactly.
3. Change the body to `Harmless native note updated.` and click **Update selected**.
4. Search for `DEMO-20261011`, then search for `updated`, then clear the search.

**Pass criteria:** Add and update show success status; the selected editor and list show the exact synthetic values. Search matches title and body case-insensitively and updates the matching-count label. Existing notes are neither opened nor modified.

### 20. Notes Markdown export and retained review record

**Result:** See [demo test results](demo-test-results.md).

1. With the synthetic note present, click **Export Markdown** and save to a new disposable demo folder as `DEMO-20261011-notes.md`.
2. Verify through the Save dialog/File Explorer that the file exists and is non-empty; do not open/export unrelated note content in evidence.
3. Leave `DEMO-20261011 note` in Notes for the user's review.

**Pass criteria:** Export reports success and creates a Markdown file. The DEMO note remains visible and unchanged for review. The disposable export may be deleted after its evidence is captured.

### 21. Explicit memory fact create, update, and retain

**Result:** See [demo test results](demo-test-results.md).

1. Open Memory and verify the plain-text/local/provider-sharing disclosure.
2. Preserve an existing personal profile. If Memory is initially empty, an explicitly labeled DEMO profile may be created and retained; this run used DEMO-Buddy.
3. Add `DEMO-20261011 prefers harmless demo data.`
4. Select that exact fact, change it to `DEMO-20261011 memory updated.`, and click **Update selected**.
5. Leave the updated DEMO fact in Memory for the user's review.

**Pass criteria:** Add/update show the expected status and count change. Selection loads the exact fact into the editor. Earlier personal facts/profile remain intact, and the DEMO fact/profile remain available for review.

### 22. Memory export/clear safeguards without exposing data

**Result:** See [demo test results](demo-test-results.md).

1. Export only if Memory contains the owned DEMO profile/facts and no earlier personal content. This run began with no memory file; its native DEMO export is retained for review.
2. Click **Clear all memory** and choose **No**.
3. Verify the visible count and profile fields remain unchanged.

**Pass criteria:** A DEMO-only JSON export matches the visible profile/facts. Clear uses an explicit destructive warning with No as the safe/default choice. Cancelling preserves all displayed memory. Exporting earlier personal memory is excluded.

### 23. Local typed note command intercept

**Result:** See [demo test results](demo-test-results.md).

1. In Chat, enter `note: DEMO-20261011 typed command note` and send it while the screen is harmless.
2. Open Notes and locate the exact saved note.
3. Leave the DEMO note in Notes for the user's review.

**Pass criteria:** Home reports that the note was saved locally and that no screenshot or note content was sent to a provider. The exact note appears in Notes and remains available for review. If capture/provider activity occurs, mark Fail.

### 24. Local typed remember command intercept

**Result:** See [demo test results](demo-test-results.md).

1. In Chat, enter `remember: DEMO-20261011 typed memory` and send it.
2. Open Memory and locate the exact fact.
3. Leave the DEMO fact in Memory for the user's review.

**Pass criteria:** Home reports explicit local-memory storage. The exact fact appears in Memory and remains available for review. The command does not start an Agent task or provider request.

## E. Task workspace and permission UX

### 25. Task timeline from the sandbox demo

**Result:** See [demo test results](demo-test-results.md).

1. After case 15, open Tasks and select the synthetic sandbox run.
2. Inspect its state label and the Step, State, and What happened columns.
3. Compare the listed actions to the confirmations and actual sandbox result.

**Pass criteria:** The newest run is selected, its access mode is recorded as ConfirmEach/Approve each, events remain ordered, and terminal state matches what actually happened. A failure is visibly styled and is not presented as success.

### 26. Task pause, resume, and stop controls

**Result:** See [demo test results](demo-test-results.md).

1. Start a fresh built-in sandbox action demo.
2. At the first safe action boundary, bring Home forward; verify the current task pauses.
3. Open Tasks and click **Resume task**. Wait through the visible countdown and approve only the next sandbox action.
4. Click **Pause task**, then **Stop task** before another action.

**Pass criteria:** Home activation or Pause records `paused` and prevents the next action until Resume. Resume gives time to return focus to the intended sandbox. Stop records a terminal stopped state and performs no later action. Completed input remains visible and is not claimed to be undone.

### 27. AG-UI-shaped task export

**Result:** See [demo test results](demo-test-results.md).

1. Select only a synthetic sandbox run in Tasks.
2. Click **Export events** and save `DEMO-20261011-task.ndjson` in the disposable demo folder.
3. Verify the file exists and contains newline-delimited JSON records using a safe text viewer.

**Pass criteria:** Export creates bounded NDJSON with a run start, ordered progress/step records, and the correct terminal event. It is labeled as an export; no live CopilotKit or AG-UI connection is implied. Delete the disposable export after evidence.

### 28. Clear-task-history safeguard

**Result:** See [demo test results](demo-test-results.md).

1. Click **Clear tasks**.
2. Choose **No**.

**Pass criteria:** The dialog states that current work would stop and saved task text/progress would clear. Cancelling leaves the run list unchanged. Do not clear existing task history in this pass.

### 29. Preview mode — backend-tested, user-manual acceptance required

**Status: Skipped by instruction**

Computer use does not change the task-access security setting. Focused backend policy tests may verify that Preview refuses execution. A later user-run manual acceptance can select Preview and request a harmless plan while observing that no input occurs.

**Manual pass criteria:** The task produces a plan/preview and performs no mouse, keyboard, scroll, file, or external action. The selected mode is recorded in Tasks. Backend evidence alone does not establish native UX acceptance.

### 30. Auto and Full access — backend-tested, user-manual acceptance required

**Status: Skipped by instruction**

Computer use does not select either elevated mode. Focused backend policy tests may cover the Auto heuristic/hotkey branches and the Full-access bypass branch. The user must personally perform any native warning/behavior acceptance.

**Manual pass criteria:** Auto visibly warns about task-plan approval and heuristic limits, then presents a task plan before actions. Rejecting the plan performs no mutation. Full access visibly warns that it can send, publish, delete, or change data and that Stop cannot undo completed actions. No computer-use run may claim these criteria passed.

## F. Voice paths

### 31. Home voice button with local note command

**Result:** See [demo test results](demo-test-results.md).

1. Confirm the intended microphone is available.
2. Click **Listen to my voice** and say exactly `note: DEMO-20261011 voice note`.
3. Confirm the live `Hearing:` text/overlay reflects the phrase, then click **Send voice request**.
4. Verify the note in Notes and leave it available for the user's review.

**Pass criteria:** Listening state, local transcript, and button label change visibly. The final command saves one exact local note without screen capture/provider submission and retains it for review. If recognition is wrong, record the transcript as a Fail rather than correcting the evidence.

### 32. Global hold-to-talk shortcut

**Result:** See [demo test results](demo-test-results.md).

1. With a harmless app focused, hold the saved shortcut and say `remember: DEMO-20261011 shortcut memory`.
2. Release the shortcut.
3. Reopen Home from the tray, verify the recognized command and the Memory fact, and leave the fact available for the user's review.

**Pass criteria:** Press begins local listening from another app; release finalizes once; the overlay does not steal focus. The exact fact appears once and remains available for review. A missing configured microphone fails explicitly instead of silently using a different device.

### 33. Voice task-control commands

**Result:** See [demo test results](demo-test-results.md).

1. Start a fresh sandbox task and pause it at a safe action boundary.
2. Use voice for `pause task`, `resume task`, and `change task: stop after the next sandbox step` only while the synthetic task is active.
3. Finish with `stop task`.

**Pass criteria:** Each recognized command produces an explicit assistant acknowledgement and matching Tasks event. Change-task updates guidance and leaves the task paused until resume. Stop prevents any later sandbox action. No command is treated as a general provider question.

## G. Existing-provider screen help, only when already connected

These cases never initiate sign-in or enter credentials. Mark them Blocked when no provider is already connected. Before each case, place only harmless content on the visible monitors and acknowledge the screen-sharing disclosure.

### 34. Typed Ask about the screen

**Result:** See [demo test results](demo-test-results.md). **Prerequisite:** an eligible provider is already connected.

1. Open a harmless local test window with a distinctive visible sentence.
2. In Home choose **Ask about my screen**, type `Read the distinctive sentence and point to it.`, and send.
3. During the three-second handoff, focus the test window.

**Pass criteria:** Home minimizes for handoff, the selected provider returns a reply, and the overlay marks the actual sentence without changing the target. Chat shows the user request and assistant reply. Inspect the target mark visually; text alone is insufficient.

### 35. Selected-area provider request

**Result:** See [demo test results](demo-test-results.md). **Prerequisite:** an eligible provider is already connected.

1. Put a harmless unique sentence in a local test window and type `Read only the selected sentence.` in Home.
2. Click **Select screen area** and drag tightly around the sentence.
3. Wait for the answer and target mark.

**Pass criteria:** Only the selected pixels are submitted by this path; the reply matches the unique sentence and the mark maps into the selected region. Home returns after completion. Exact requested shape and correct target placement are recorded separately.

### 36. Help with this app

**Result:** See [demo test results](demo-test-results.md). **Prerequisite:** an eligible provider is already connected.

1. Show a harmless OpenClicky screen.
2. Click **Help with this app**.

**Pass criteria:** The answer describes a visible OpenClicky control and marks a relevant control without activating it. The request excludes unrelated old task history. A plausible answer with a wrong mark is a Fail.

### 37. Official web help and HTTPS link handling

**Result:** See [demo test results](demo-test-results.md). **Prerequisite:** an eligible provider is already connected.

1. Show a harmless supported application screen.
2. Click **Find official web help**.
3. Verify the reply contains an existing HTTPS source and marks a relevant visible next control.
4. Click the HTTPS link only if the root task has approved opening the system browser.

**Pass criteria:** A provider/web-search result appears with a clickable HTTPS link; non-HTTPS links do not launch. The visible mark is correct and no control is activated. Source existence does not by itself validate every generated instruction.

### 38. In-flight cancellation and window-change guard

**Result:** See [demo test results](demo-test-results.md). **Prerequisite:** an eligible provider is already connected.

1. Start a harmless screen Ask likely to take several seconds.
2. Press Stop while the provider request is pending.
3. In a separate run, wait for a visible pointing/responding overlay, then switch to a different harmless window.

**Pass criteria:** Stop returns to Ready and no late reply/TTS appears. Changing the response window cancels and hides the mark rather than leaving guidance over a different app. Record Blocked if the response completes too quickly to test cancellation.

## H. Connections and provider boundaries

### 39. Connections disclosure and OpenCode negative health check

**Result:** See [demo test results](demo-test-results.md).

1. Open Connections.
2. Verify the paused-account, Gmail-consent, ChatGPT-token separation, and OpenCode task-handoff disclosures.
3. With no OpenCode server deliberately started for this case, leave the endpoint at `http://127.0.0.1:4096` and click **Check local OpenCode**.

**Pass criteria:** The check sends no prompt and launches no task. If no server is listening, the UI shows a bounded `Not connected` message and re-enables the button. It does not imply Gmail, Composio, Nango, OpenWork, or OpenCode provider authorization exists.

### 40. OpenCode positive health check, isolated fixture only

**Result:** See [demo test results](demo-test-results.md). **Prerequisite:** the isolated fixture is available.

1. Run only if the root task has already started an isolated password-free loopback OpenCode health fixture intended for this UI check. Do not start it through the UI and do not connect accounts.
2. Enter its exact loopback origin and click **Check local OpenCode** once.
3. Stop the fixture after evidence.

**Pass criteria:** The UI shows the actual reported version and still states that task handoff/provider consent are required. No session, AI prompt, tool, event stream, or account access occurs. Non-loopback, credential-bearing, path, query, fragment, redirect, or oversized responses are rejected by the backend contract tests rather than tried in the desktop demo.

## Backend protocol and focused tests — separate from native UX

These checks are implemented test/harness coverage, not user-visible integration demos. Their current command output and counts belong in the [results report](demo-test-results.md).

| ID | Backend scope | What it can establish | What it cannot establish | Status | Actual result / evidence |
| --- | --- | --- | --- | --- | --- |
| B01 | `AgentAccessPolicyTests` | Preview blocks execution; ConfirmEach confirms; Auto keyword/rationale/hotkey decisions; FullAccess policy branch; pause/resume cancellation | Safe real-world autonomy, correct model rationale, or Full-access acceptance | See [results report](demo-test-results.md) | See results report |
| B02 | `TaskHistoryStoreTests` | Bounded task persistence and AG-UI-shaped NDJSON lifecycle/progress encoding | Live AG-UI transport, CopilotKit rendering, or visible export UX | See [results report](demo-test-results.md) | See results report |
| B03 | `LocalNotesStoreTests` | CRUD, search, limits, unreadable-file preservation, Markdown export | Native layout, Save dialog, or voice recognition | See [results report](demo-test-results.md) | See results report |
| B04 | `CompanionMemoryStoreTests` | Explicit profile/fact persistence, bounds, deletion, export, unreadable-file preservation | Personalization quality or user consent for actual facts | See [results report](demo-test-results.md) | See results report |
| B05 | `TaskVoiceCommandsTests` | Exact parse rules for stop/pause/resume/note/remember/change-task | Physical microphone accuracy or task timing | See [results report](demo-test-results.md) | See results report |
| B06 | `OpenCodeTaskClientTests` | Loopback-only origin validation, Basic auth handling, bounded JSON, redirect rejection, health/session/prompt/messages/status/abort request shapes | Managed process lifecycle, live AI prompt, SSE, permissions, auth, or task handoff | See [results report](demo-test-results.md) | See results report |
| B07 | Screen guidance/annotation tests | Coordinate mapping, selected-region bounds, invalid-mark rejection, app-switch debounce/cooldown | Correct live model target on arbitrary apps | See [results report](demo-test-results.md) | See results report |
| B08 | Provider protocol tests | Bounded response parsing, model-list and ChatGPT/OpenRouter request contracts, error/refusal handling | Account eligibility, live quota, current provider service, or auth UX | See [results report](demo-test-results.md) | See results report |
| B09 | Voice/audio focused tests | Resampling, cancellation, local recognition fixtures, configured-device failure behavior | Current physical microphone, room noise, accent, and long-request acceptance | See [results report](demo-test-results.md) | See results report |
| B10 | Desktop smoke harnesses | Capture bytes, passive overlay geometry/focus, owned sandbox input, secret-store round trip | General desktop success or clean-machine installation | See [results report](demo-test-results.md) | See results report |

## Built controls intentionally not exercised in this pass

These controls are part of the current UI inventory, but the bounded demo rules exclude their external or destructive effect. Their presence is inspected in the cases above; their effect is not accepted.

| Built control/function | Why the effect is excluded | Audit status |
| --- | --- | --- |
| Continue with ChatGPT / Sign out | Would change account authorization | Skipped by instruction |
| OpenRouter API-key save | Would require credential entry or replacement | Skipped by instruction |
| Refresh ChatGPT models / Manage usage | Uses the signed-in account or opens its external account page | Skipped by instruction |
| Download accurate voice | Downloads a 142 MB model and changes local runtime state | Skipped by instruction |
| Overwrite an existing personal companion profile | Could replace personal name/personality/instructions | Excluded; initially empty DEMO profile was tested |
| Export earlier personal companion memory | Would copy personal memory into a new file | Excluded; DEMO-only native export passed |
| Clear all companion memory | Would irreversibly remove existing profile/facts; only the cancelled confirmation is tested | Skipped by instruction |
| Clear conversation and task history | Would remove existing history; only the cancelled confirmation is tested | Skipped by instruction |
| Clear task history | Would remove existing task records; only the cancelled confirmation is tested | Skipped by instruction |
| Persist-history, proxy, GDI, local/cloud speech, model, and protected-title setting changes | Could alter the user's working configuration; controls are inventoried without changing saved values | Skipped by instruction |
| Full-access execution | Explicitly excluded from agent selection for this run | Skipped by instruction |

## Present in the UI or research, but not built as an end-to-end integration

Do not create native demo cases that imply these are operational:

| Capability | Current boundary | Audit status |
| --- | --- | --- |
| OpenCode task execution in Home | Bounded client and health UI exist; no managed runtime lifecycle, provider connection, live prompt, SSE, permission handoff, or native task binding | Not built |
| OpenWork integration | Research/reference architecture only; no OpenWork UI/server is embedded | Not built |
| Live AG-UI transport | Stored task runs can export AG-UI-shaped NDJSON only | Not built |
| CopilotKit UI/runtime | No live CopilotKit runtime or component integration in WinForms | Not built |
| OpenIntelligentUI / generated HTML / 3D | No WebView2 renderer, iframe sandbox, generated UI, or Three.js scene in the app | Not built |
| Composio or Nango accounts | Comparison and disclosures only; account work is paused | Not built |
| Gmail or other connected apps | No account connection, token, tool execution, trigger, or sync | Not built |
| OpenWhispr application | Interaction ideas informed the native voice/Notes work; its Electron app is not embedded | Not built |
| Continuous duplex voice, meeting capture, or general dictation | Current voice is push-to-talk request/command capture | Not built |
| Schedules/routines | No user-visible scheduler or background routine runner | Not built |
| Full-access acceptance | Code path and warning exist, but the user prohibited agent selection during this run | Skipped by instruction |
| Provider sign-in/sign-out and API-key entry | UI exists, but account and credential changes are excluded from this run | Skipped by instruction |

## Result reporting

See [demo-test-results.md](demo-test-results.md) for the current category totals, individual case outcomes, evidence, defects, fixes, and retests. For each failure, that report should retain the exact build, case ID, starting state, visible destination, expected result, actual result, screenshot/screen recording path, whether any external effect occurred, fix/worktree state, and retest result.

Do not convert a backend pass, status label, or model statement into user-visible acceptance without inspecting the actual native control or destination.


