# OpenClicky 0.4.1 demo results

Tested on Ahmad's Windows laptop on 11–12 October 2026, using native Computer Use and the existing dedicated test harnesses. This is local preview acceptance, not a public release or a claim of general desktop reliability.

The [repeatable 40-case checklist](demo-test-plan.md) inventories the current controls. **216 automated tests passed, with zero failures or skips.** Native testing found defects in chat scrolling, window restoration, task selection/status, approval/picker discoverability, shortcut labels, privacy copy, annotation readability and web-help relevance. The fixes and proof limits are recorded below.

`Pass` applies to the bounded behavior actually observed. `Partial` means part of a checklist case remains unverified. `Blocked` means a prerequisite was unavailable. `Manual required` identifies controls that Computer Use cannot accept on the user's behalf. None of these statuses accepts an integration that has not been built.

Current 40-case totals: **21 Pass, 10 Partial, 4 Blocked, 5 Manual required**. Physical voice accounts for the four blocked cases. The report preserves untested portions instead of counting backend checks as desktop acceptance.

## Build and evidence

- Source: dirty `codex/windows-product` worktree based on `fd62fb37927ffef2156442f9c151e99eeb64c85c`; prior user changes were preserved.
- Executable: `artifacts/exe/win-x64/OpenClicky.exe`, file version `0.4.1.0`, product version `0.4.1+fd62fb37927ffef2156442f9c151e99eeb64c85c`. The local fixes are newer than that base commit.
- Existing signed-in ChatGPT connection: Agent `gpt-6.1-sol`, screen replies `gpt-6-luna`. No sign-in, sign-out, API-key entry or account connection was automated.
- The live desktop-action fixture was an owned OpenClicky sandbox. Its primary window and secondary-monitor cover displayed only `DEMO-20261011` content. All real input remained in that fixture and used normal production approvals.
- Native screenshots were inspected in the Computer Use session. They are session evidence, not a saved video or a screenshot collection distributed with this report.
- Package checks: [portable 0.4.1 ZIP](../artifacts/OpenClicky-0.4.1-win-x64.zip) contains exactly the EXE, README, LICENSE and third-party notices. Its EXE and the [unsigned MSIX](../artifacts/msix/OpenClicky.msix) payload match the published canonical/alias SHA-256 `9725EBA330EB849C841649ED52D85CC55BB9F77E6BA1D20D7F42AD525FA597E3`. MSIX manifest is `0.4.1.0` and its three expected logo assets exist. [Package receipt](../artifacts/demo-tests-2026-10-11/package-receipt.json) records full hashes/sizes. Packing was verified; installation/signing was not.
- Durable local receipts: [backend results](../artifacts/demo-tests-2026-10-11/backend-results.md), [216-test TRX](../artifacts/demo-tests-2026-10-11/core-tests.trx), [native Notes export](../artifacts/demo-tests-2026-10-11/notes-native.md), [native Memory export](../artifacts/demo-tests-2026-10-11/memory-native.json), and [native task-event export](../artifacts/demo-tests-2026-10-11/task-native.ndjson). These artifacts are ignored by Git; they are local review material.

## Native cases

| ID | Function | Status | Observed result and remaining limit |
| --- | --- | --- | --- |
| 1 | Launch, workspace and version | Pass | Published EXE opened a usable Home with Chat, Tasks, Memory, Notes and Connections. Normal/maximized layouts and the visible `v0.4.1` footer were inspected. Startup and restoration after a live Ask now show the latest chat without manual scrolling. |
| 2 | Ask/Agent composer and welcome chips | Partial | Both modes were used; blank Send stayed Ready without a request. Shift+Enter visibly made a two-line unsent draft. Welcome chips were not exposed because saved history was retained. |
| 3 | Access choice inventory | Pass | Dropdown visibly listed Preview, Approve each action, Auto and Full access. Escape closed it and left Approve each action selected. This does not accept elevated-mode execution. |
| 4 | Home close and tray reopening | Partial | Closing Home kept its process running. The tray's double-click/menu reopening paths were not exposed by the supported computer-use surface. |
| 5 | Tray capture diagnostic | Partial | The real WGC capture harness passed on two monitors. The tray menu/dialog entry point was not tested through native UI. |
| 6 | Stop while listening | Blocked | The saved AI Wireless Microphone was unavailable. Listening-stop variants cannot be accepted from a speech fixture. Stop during a real spoken AI reply did cancel and return Ready; see case 38. |
| 7 | Tray Quit | Manual required | Tray Quit was not operated. Restarting the owned demo process for builds is not proof of tray Quit behavior. |
| 8 | Settings/provider layout | Pass | All four Settings tabs and the current ChatGPT connection/model panel were inspected. Alternate-provider authorization and sign-in controls were excluded. |
| 9 | Microphone list and live level meter | Blocked | Refresh/current-device state and the unavailable-device error were observed. Wireless selection was preserved; no other mic was silently substituted. A physical speech level-meter pass remains open. |
| 10 | Local voice/model controls | Partial | Settings honestly showed the installed local Whisper model. Real inference, reference audio, silence, cancellation and resampling passed separately. Cloud/OpenRouter switches were not changed. |
| 11 | Shortcut editing | Partial | Draft Ctrl+Shift+F9 visibly read `Ctrl+Shift+F9` after the fix; Reset restored the Win+Alt+Space draft. No remapped shortcut was saved or physically exercised. |
| 12 | Privacy copy and history safeguard | Pass | Disclosure distinguishes full-screen and selected-area sharing. Clear confirmation names conversations and task history and says Notes/explicit Memory remain. No was chosen; history remained. |
| 13 | Diagnostics and settings saving | Partial | Advanced controls and Refresh diagnostics worked. Closing preserved preferences. Unchanged Save and a settings-upgrade round trip were not accepted through native UI in this pass. |
| 14 | Offline buddy, pointing and trail | Pass | Native pointing demo visibly showed the round companion, target label and fading trail, then returned Ready. Spoken-response interruption was tested separately. Human hearing/audio quality was not measured. |
| 15 | Offline desktop actions | Pass | Three separate production approvals led to one actual green-button click and exact `hello` in the owned sandbox. Actual destination controls were inspected. |
| 16 | Decline and changed-target guard | Pass | Declining stopped a fresh run with unchanged controls. Switching the target caused re-planning or rejection of old input rather than typing into another app. A declined live attempt left counter zero and the field empty. |
| 17 | Selected-area cancellation | Pass | The actual selector is now discoverable. Escape targeted that window, closed it and restored Ready with no provider request. Minimum-size/clipping behavior passed the focused geometry check. |
| 18 | Automatic app-switch guide | Manual required | Guide remained off. Debounce/cooldown/cancellation are backend-tested; enabling its screen-sharing setting and accepting its native behavior need a user-run check. |
| 19 | Notes CRUD, validation and search | Pass | Created/edited `DEMO-20261011: grocery list`; native body search for `bread` found it. Editor showed the changed body including `oranges`. Blank title produced an inline validation error. |
| 20 | Notes export and retained records | Pass | Native Save dialog exported Markdown containing the edited demo and retained earlier note. Demo records remain for review at the user's request; deletion was not exercised. |
| 21 | Memory/profile CRUD | Partial | Native demo profile and fact creation/editing passed, including coral-to-teal edit. Delete was cancelled at its safeguard; actual deletion was not exercised. |
| 22 | Memory export and clear safeguards | Pass | Native JSON export matched the demo profile/fact. Delete/Clear all confirmations were cancelled and records remained. This is safeguard/export acceptance, not a destructive-clear pass. |
| 23 | Typed local note command | Pass | Local command saved `DEMO-20261011: native command saved this harmless note.` once. The note was displayed after restart; no provider was needed. |
| 24 | Typed local remember command | Pass | Local command saved the demo test-number fact once. Native Memory displayed both teal and test-number-73 facts after restart. |
| 25 | Task narration, persistence and selection | Pass | Native timeline showed plan, observations, separate approvals and terminal events. Newly inserted runs selected themselves. Completed remained completed when Home opened during final narration after the fix. |
| 26 | Task pause/resume/stop | Partial | Home activation paused at an action boundary; Resume proceeded with fresh grounding. Decline stopped a task. Native Stop during a completed task's final narration returned Ready and preserved completed status; physical voice/active-task Stop acceptance remains open. |
| 27 | AG-UI-shaped export | Pass | Native export produced 59 parseable NDJSON events: one run start, one run finish, 19 custom, 19 step starts and 19 step finishes. This is a stored export, not a live AG-UI/CopilotKit bridge. |
| 28 | Clear task history safeguard | Pass | Native warning stated that saved task text/progress would clear and current work would stop. No was focused and chosen; run list remained. |
| 29 | Preview execution | Manual required | Policy tests cover refusal to execute. Computer Use did not change the access setting; native Preview behavior remains a user check. |
| 30 | Auto/Full warnings and execution | Manual required | Backend policy branches passed. Neither mode was enabled by the agent. Warnings and real execution need explicit user-run acceptance; heuristics/title guards are not security boundaries. |
| 31 | Physical voice note | Blocked | AI Wireless Microphone was unavailable. Prior user confirmation of one accurate phrase is historical, not a current voice-note pass. |
| 32 | Physical hold-to-talk shortcut | Manual required | The saved shortcut uses the Windows key, which this Computer Use skill cannot inject. Injected keys also do not establish physical-hook acceptance. |
| 33 | Physical voice task steering | Blocked | Parser and task-control checks passed, but pause/resume/change/stop spoken through the selected device were not possible while it was disconnected. |
| 34 | Typed live Ask and drawings | Pass | GPT-6-Luna read exact `DEMO-20261011 live AI passed`; actual UI stayed at counter one. Native circle surrounded Green, rectangle surrounded Type target and arrow pointed to Clear field. All three were visibly inspected after the bubble fix. |
| 35 | Selected-area live request | Pass | Selected Notes returned the correct explicit-memory color and Home retained its maximized state. After calibrating the tool's spanning-window coordinates, the final selected sandbox area returned exact field text and visibly circled Green/boxed Type target. Earlier blank selections were not counted as passes; broader DPI behavior remains open. |
| 36 | Help with this app | Pass | Native button generated fresh screen help and described the owned fixture. Final retest visibly pointed to and outlined Clear field without changing controls. Earlier window-change cancellation remains recorded separately. |
| 37 | Official web help and links | Partial | Real web search ran. Initial sandbox answer cited inapplicable ChatGPT help; revised answer identified the local fixture and included no URL. Explicit Microsoft lookup returned a relevant source, independently verified below. Native link opening remains untested; general relevance still depends on the model. |
| 38 | In-flight cancellation/window guard | Partial | Changed-window reply was discarded; switching windows during a visible reply cleared it. Home Stop interrupted a long real spoken reply and returned Ready (`turn_cancelled`). Several pending-request attempts completed before the UI click; those do not prove network-pending cancellation. Focused cancellation tests passed. |
| 39 | Connections and negative health | Pass | Native Connections disclosed paused account work and separate consent. Default loopback endpoint reported not connected/refused and re-enabled checking without starting a server or task. |
| 40 | Positive isolated OpenCode health | Pass | Real isolated OpenCode 1.17.18 returned healthy; native UI still said task handoff/provider consent required. Root stopped that owned fixture after checking. No AI prompt/account/tool execution occurred. |

## Failures, fixes and retests

| Defect | Smallest fix | Retest evidence |
| --- | --- | --- |
| Selected-area return lost maximized Home state | Preserve last non-minimized state and restore through ShowHome | Actual maximized Home remained maximized after a live selected-area reply; focused state assertions pass. |
| Replies appended on a hidden Chat tab were not shown when returning | Defer scroll until the Chat tab is visible | Notes-to-Chat showed the newest own demo reply. |
| Selected Chat did not follow new messages while the entire Home was minimized | Snapshot whether the reader was at latest before hiding; defer scrolling until restoration | Final live pointer/drawing request returned with its exact new prompt and reply visibly at the bottom without Ctrl+End. Focused checks preserve a reader intentionally above latest. |
| Startup displayed old chat first | Consume one initial-scroll flag after the first visible layout | Final EXE startup showed the latest saved chat at the bottom. Later reopen does not force a reader away from old text. |
| Pausing left a stale action overlay; new runs could leave an old run selected | Hide overlay on pause; select only newly inserted newest runs | Native pause removed the stale overlay; current live run selected itself. Existing historical selection has focused preservation checks. |
| Completed run became paused, then could become stopped while TTS drained | First terminal state is authoritative for later progress | Native completed-to-pause and completed-to-Stop races passed. Focused checks also reject failed-to-stopped and terminal-to-paused while permitting active cancellation. Old recorded runs were not rewritten. |
| Approval and region-picker windows were absent from app switching/automation inventory | Show those two interactive windows in the taskbar | Native approvals were discoverable and completed three real actions; actual selector could receive Escape. Passive overlays remain non-activating. |
| Modified function-key label displayed a VK code | Label F1 through F24 using the existing key mapping | Five focused cases added; native Ctrl+Shift+F9 label and Reset passed. |
| Footer hardcoded old release; privacy/history text understated scope | Derive footer from product version; correct sharing/clear labels and confirmation | Native v0.4.1 and revised Privacy screen/No confirmation inspected. |
| Spoken bubble hid arrows and field outlines | Move only the main response bubble into a free band around same-screen annotation bounds | Pure top/bottom/negative-monitor/no-mark geometry checks passed. Native POINT plus three marks placed the response below the annotations and left their targets clear; receipt below. Dense marks can still leave no free band. |
| Web help included a source it explicitly said was irrelevant | Make source inclusion conditional; local-fixture help has no unrelated URL | Existing request-shape checks passed. Native sandbox retest described the fixture and included no URL. This is improved model guidance, not a semantic guarantee. |

No external app, email, campaign, account or user file was modified by the desktop input tests. Live screen questions sent the visible demo pixels/request and explicit companion context to ChatGPT. Local voice fixtures and Windows TTS stayed local. Public web lookup used the provider's search tool.

### Final live retests — 12 October 2026

- **01:26:28, pointer plus three marks:** GPT-6-Luna completed HTTP 200 with point `(93,93)`, circle `(9,69,177,51)`, rectangle `(9,132,453,57)` and arrow `(333,156,258,96)`. The actual native screenshot showed Green circled, Type target boxed, an arrow to Clear field and the main response below their bounds. Destination still showed counter one and exact `DEMO-20261011 live AI passed`. Home then showed this new request/reply without manual scrolling.
- **01:27:38, terminal-status Stop:** GPT-6.1-Sol performed an accessibility observation and then `done` in a read-only task. While its 100-word final narration was active, Tasks visibly showed `completed · ConfirmEach`. Clicking **Stop task** returned Ready, disabled global Stop, and left that run completed. After the final restart, native Tasks still showed it completed. This validates narration cancellation/status preservation; it is not another click/type mutation pass.
- **01:28:38, built-in help button:** GPT-6-Luna completed with point `(264,93)` and rectangle `(207,72,117,42)`. Native screenshot visibly showed the companion beside Clear field and its outline around that button. Counter and text remained unchanged.
- **01:32:07, selected-area drawings:** The actual selection was `(3,64,462,173)`. GPT-6-Luna read exact `DEMO-20261011 live AI passed`, returned circle `(17,72,158,40)` and rectangle `(16,152,440,32)`, and both were visibly placed on Green and Type target. Input did not change. Sky's screenshot origin/window input coordinates required calibration using the new metadata; this is not a claim of universal mixed-DPI acceptance.
- **01:17:29, explicit public lookup:** GPT-6-Luna performed one web search, answered that Ctrl+Shift+Esc opens Task Manager, and included a Microsoft HTTPS link. The returned link resolved to [Microsoft's Windows keyboard shortcuts](https://support.microsoft.com/en-us/accessibility/windows/keyboard-shortcuts-in-windows), whose Task Manager entry independently confirms the answer. The reply/link appeared in Home. Opening that link through Home was not tested.

## Automated and harness checks

Final root commands:

```powershell
dotnet build OpenClicky.sln -c Release
dotnet test tests/OpenClicky.Core.Tests/OpenClicky.Core.Tests.csproj -c Release --no-build --logger 'trx;LogFileName=core-tests.trx' --results-directory artifacts/demo-tests-2026-10-11
dotnet run --project apps/windows/OpenClicky.App.SecretStore.Smoke -c Release --no-build -- --home-ui-state
dotnet run --project apps/windows/OpenClicky.Overlay.Smoke -c Release --no-build -- --geometry-only
./scripts/publish-exe.ps1
```

Build: zero warnings/errors. Core suite: 216 passed, zero failed/skipped, eight seconds. Both focused state/geometry checks passed. The final Home-only minimize/scroll change then passed the exact Release build in 2.97 seconds and the expanded `--home-ui-state` check; unchanged core/provider tests were not rerun. Publish succeeded and the published app passed the live retests above. The [backend receipt](../artifacts/demo-tests-2026-10-11/backend-results.md) contains the separate harness commands, results and limitations.

| Scope | Result | Proof limit |
| --- | --- | --- |
| B01–B08: access, tasks/export, Notes, Memory, command parsing, OpenCode contract, guidance, provider/auth protocol | Included in the 216-test pass | No per-class count is inferred from the aggregate. Tests do not establish arbitrary desktop autonomy or current account eligibility. |
| Whisper runtime/model | Integrity, generated preview/final, human reference sample, silence, precancel/in-flight cancel, resampling and missing-device checks passed | Recorded/reference speech is not the selected physical microphone or room-noise acceptance. |
| Windows speech/TTS | Generated recognition and local TTS cancellation passed; cancellation took 359 ms | Synthetic recognition does not resolve the user's earlier real-mic inaccuracy. |
| Synthetic secret store | Small Credential Manager and large DPAPI round trips passed | Only dedicated fixture keys were used; user credentials were not read or replaced by this check. |
| WGC capture | Two monitor images, positive JPEG bytes/dimensions/magic | Capture success does not prove screenshot relevance or provider behavior. |
| Passive overlay | Native geometry/foreground smoke passed; final pure bubble/region geometry passed | The native harness closed before a surviving screenshot. Separate actual app screenshots establish the bounded visible marks. |
| Input smoke | Dedicated auto-confirm harness observed one actual click and exact `hello` | Auto-confirm remains only in the harness; native production demos independently reviewed each mutation. |
| Protected OpenCode runtime | Real 1.17.18 health/create-empty-session/messages/status/abort passed in isolated local state | No live AI prompt, engine tool, provider consent, SSE, managed lifecycle or Home task handoff was accepted. |

## Retained review data and remaining scope

The user explicitly answered **Keep them for review**. Two DEMO notes, two DEMO memory facts and the DEMO-Buddy profile therefore remain. Earlier notes, conversations, task records, settings, credentials and logs were preserved. Do not run the checklist's former cleanup instructions against these records without new authorization. Demo profile/latest facts will continue to be included in future AI requests until the user edits or removes them.

The owned sandbox and isolated OpenCode server were stopped after testing. The final published app was restarted and left Ready on Memory for review; DEMO-Buddy, the teal fact and test-number-73 fact were retained. Its task status survived restart. No further provider request was sent during this final review.

Settings values were preserved individually. Serialization added the new default `ConfirmEach` access property; the settings file is not byte-identical to its pre-upgrade backup. The unavailable wireless microphone ID was retained. No alternate microphone, Full access, Auto mode, guide-sharing toggle or provider was selected by the agent.

The Computer Use skill prohibits changing in-app security/privacy settings and injecting Windows-key shortcuts. Accordingly, elevated access/guide/physical-shortcut acceptance remains manual. The applicable instruction is **“Do not change Windows security settings, Windows privacy settings, or any in-app security or privacy settings.”** See the installed [Computer Use SKILL.md](C:/Users/ahmad/.codex/plugins/cache/openai-bundled/computer-use/26.1007.21434/skills/computer-use/SKILL.md) and its [guidance](C:/Users/ahmad/.codex/plugins/cache/openai-bundled/computer-use/26.1007.21434/docs/guidance.md). This is a tool-use restriction, not a claim that the product lacks those choices.

Still **not built end to end**: OpenWork/OpenCode task handoff, live AG-UI/CopilotKit transport, OpenIntelligentUI/3D renderer, Composio/Nango/Gmail accounts, scheduling/routines, continuous duplex voice, general dictation and automatic learning. Account integrations remain paused at the user's instruction. The health client, stored event export and local Notes/Whisper behavior do not amount to embedding those repositories' full products.

Next acceptance should reconnect AI Wireless Microphone and run voice-note, shortcut and task-steering cases; manually verify access warnings/Preview; then check tray entry points, clean-machine installation and broader app/DPI behavior. The app remains an unsigned preview. Installation, signing, clean-machine upgrades and public CI/release were not accepted in this run.
