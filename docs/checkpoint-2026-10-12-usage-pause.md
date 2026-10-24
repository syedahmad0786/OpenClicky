# Safe checkpoint — usage guard pause, 12 October 2026

Build work stopped after the usage-guard notification at approximately 14:51 UTC. A fresh account check confirmed the Codex weekly window at 100% used (0% remaining). The API still reported ordinary usage allowed and available credits; no credit purchase, reset or automatic continuation was initiated. Preserve this checkout and resume only on the user's instruction.

## Current running app and last safe boundary

- `D:\OpenClicky\artifacts\phase-one-screen-guard\win-x64\OpenClicky.exe`, PID 6344, version 0.6.0.0.
- SHA-256: `D6D2A682BEDD284408FF71F2DF2B03CA875F70E966C712F85E7C4853B6336888` (alias and canonical executable match).
- The app was opened normally and Home was visibly inspected. It shows the retained calculator at Research 7 / Build 5 / Test 2, total 14. AI Wireless Microphone and Approve each action remain selected. It is idle; no recording or task was started.
- The app and its managed workspace remain open. No operating-system processes were killed.
- All three current child agents (`clipboard_fix`, `package_verify`, `workspace_finish`) had completed at the checkpoint. No child build remains active. Older agent outputs and all uncommitted work are retained.
- No builds, tests or app inputs were started after the usage notice. Only usage/authorization inspection and checkpoint documentation followed.

## Completed implementation and evidence

- OpenWork is the actual managed task foundation. OpenCode is only its internal dependency. Real ChatGPT → OpenWork → named approval → observed Sandbox click/type and installed Calculator launch passed earlier in this implementation turn.
- A separate memory-only task used only `search_memory`, cited the original work-plan conversation source `conversation#11`, and did not inspect the screen. Declining Open Calculator ended the task as stopped with no new Calculator window.
- Private dependency preseed reduced measured OpenWork startup stages to 6.7–7.9 seconds. This is task bootstrap, not physical voice latency acceptance.
- A real three-step guide detected premature Next, supported Back without undo, re-grounded after moving the Sandbox, preserved its prior text and verified the final click count.
- Native exact multiline clipboard/sequential insertion and source-bearing local memory work have evidence in the implementation ledger.
- The initial four visual types have real native acceptance: editable chart and follow-up, connected diagram, calculator, and rotatable cube. Calculator generation initially failed repeatedly; the exact request now passed after the dedicated Visualize/context repair. History was not deleted or filtered.
- Real calculator started at 3/5/2, total 10; Research changed to 7, total 14. Saving and a normal restart restored 7/5/2 and total 14. Saved cube rotation 60°, chart 12/40/8, diagram with four connections, and history were separately verified after restart. Artifact-only replies now use a truthful prepared acknowledgement.
- AI Wireless exact-device capture and advertised Windows AEC probes passed; in-memory audio was discarded. Physical speech-to-field, acoustics, interruption, repeated shortcuts and long-session performance remain unaccepted.

## Latest fix: built, but native refusal test not yet run

An additional live curve request explained the floating companion's green “Click to talk” control instead of the intended Sandbox. The model emitted a curve, but this is not accepted as target-grounded drawing. A second attempt ended with changed-window cancellation. The Sandbox's existing three text lines and click count of two stayed intact.

Source review confirmed a real guard gap: `GetCompanionTitle()` returned empty for the own process/Codex, while raw bounds/handle still allowed capture. The latest candidate adds a dedicated eligibility predicate before provider construction, bounds reads and capture, checks it again after capture and during response freshness, and leaves raw foreground helpers unchanged. Actual current-process identity covers both executable names; sibling OpenClicky names and the existing Codex exclusion are also recognized. Self-contained visuals bypass foreground inspection. Three focused regressions passed and an independent agent reviewed the fix.

Latest validation before pause: Release build **0 warnings / 0 errors**, Core **492/492**, Input **25/25**. Workspace **21/21** is unchanged from its earlier pass. Required runtime/workspace/extension assets are present. Licenses: **840 npm + 43 NuGet + 3 runtime**, zero unresolved entries. The updated portable quick-start README is included. **No ZIP or MSIX exists for this screen-guard candidate yet.**

## Retained previous package

`artifacts/phase-one-ready/` is the prior native-accepted visual candidate, before the screen guard. Do not confuse it with the currently running candidate:

- EXE: `3428A64253B9E5873D3D9640C6A8F51BFF22DD2DCDCBB838C7435303621015F5`.
- ZIP: `D63DA6F0C6AE9CE0266449593BDC5349D03DA5B3ABA21C8B8C99465B9BBF6E11`.
- Unsigned MSIX: `C073213CB28AC788A47FBFBC239F4AA576CD1395BAF53EFF9FAFDDAF80C1156A`.
- Receipt: `artifacts/phase-one-ready/ACCEPTANCE.txt`.
- The ZIP/MSIX contain the exact accepted EXE and aligned version 0.6.0.0; they are unsigned and were not installed. All older candidates remain untouched.

## Resume in this order

1. Read this checkpoint and the tail of `docs/phase-one-implementation.md`. Recheck the running app rather than restarting or rebuilding without reason. Preserve the dirty checkout and all data.
2. Use the Computer Use skill and Sky APIs for native input. Verify the new own-window refusal visibly and confirm no screen/provider call occurred for that rejected request. Then test an explicitly foregrounded external Sandbox request. Do not count a curve generated for the companion itself as success.
3. Confirm the new foreground gate does not regress self-contained visuals. Existing saved artifacts must remain intact. Only broaden tests if failures or source changes warrant it.
4. After the new native checks, create matching ZIP/MSIX from the exact screen-guard payload without rebuilding it. Update the candidate pointers, ledger and acceptance receipt with hashes and honest scope. No signing/install success is implied.
5. Await Ahmad's pending physical dictation result: Dictate into app → click Sandbox Type target → say “Please keep this as raw dictation,” pause, then “This is the second sentence,” pause → Stop recording. Both phrases must reach the actual target once, in order, with prior text intact. Preview-only is not a pass.
6. Continue the physical checklist in `docs/manual-acceptance-2026-10-12.md`: 20 conversational turns across three apps; reviewed dictation fallback; speech/task interruption and steering; ten default and ten remapped shortcuts; speaker/noise/disconnect recovery. Mixed DPI, optional Chrome extension setup, accessibility, signing and clean-machine installation remain open.

Do not delete retained demo records, history, notes, memory, packages or logs. Never enable Full access on the user's behalf. Do not automate sign-in, extension installation, security settings or Windows-key shortcuts. Phase one is not complete. The pre-existing goal tracker had a stale blocked/OpenCode description; it was not marked complete or automatically resumed during this checkpoint.
