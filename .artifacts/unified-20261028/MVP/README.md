# OpenClicky isolated MVP candidate

Revision 103138428a66f69a26fb2de494d0a749eb7d16ec. Built and staged 2026-10-28T10:22:15.118Z. This is a local developer candidate, not a signed installer or acceptance of all 99 features.

## Run on this PC

Double-click START-MVP.cmd. It launches OpenClicky.App.exe --unified-mvp using %LOCALAPPDATA%\OpenClicky-Unified-MVP. No normal account host, hooks, routines or providers are started. Original checkout/profile settings were not used for these tests.

## Verified on this candidate

- Release build: exit 0, zero warnings/errors.
- 3,081 formal tests: Core 2,596; Input 52; Recording 24 (allowlisted synthetic encoder); MeetingAudio 22; frontend 387 across 35 files. Zero failures/skips in these suites. Frontend production build also passed, with existing chunk/deprecation warnings.
- Real hidden WebView chrome check: DPI 144, corrected native height 180px for the existing 120 CSS-pixel contract; footer fits, CSS/native bounds agree, content boundary correct, isolated profile cleaned.
- Trusted UI starts at zoom 1 with user zoom/pinch controls disabled. The SDK does not notify ZoomFactorChanged for a direct host setter. That previously invalid test expectation is documented, not presented as a product pass. Untrusted page zoom was not changed.
- Owned-browser intercepted native fixture and canonical meeting synthetic fixture: exit 0. No real user page, hardware or provider/account test.
- Staged headless composition smoke: exit 0, including idle confirmed shutdown and no enumeration on construction/state reads.

## Pending acceptance

- The explicit visible production-context lifetime fixture passed: both windows opened, minimize and close/hide retained the tray loop, zero source enumeration calls, canonical idle shutdown confirmed, exit 0, own profile cleaned. This is not a long-duration stability test.
- Final native toolbar PNG inspected at DPI144: all three rows visible, native chrome180px, footer bottom707 within viewport bottom719. The exact matching already-running isolated MVP was reopened only through its browser tray command after confirming Capture: idle. Zero physical input, only two expected own context-key events; no recording controls or normal host touched. The existing process was not launched, adopted, closed or terminated by this check.
- [blocked] Independent source-file review did not complete because the reviewer could not read the worktree. No review conclusion is claimed.
- Real devices/providers/accounts, production browser profiles, physical cross-monitor transitions and user zoom gestures were not tested. Finished engine-session resume (E-S5) remains provider-limited; learned how-to list/edit (E-S8) remains deferred. Full 99-feature acceptance is false.

## Existing local speech evidence

This exact committed/staged candidate passed the file-only synthetic speech-to-transcript/store/reopen/export journey: one segment, one verbatim source-linked extract, one explicit Action-cue candidate, exit 0, no new encoder process retained. This is not general semantic decision extraction or hardware acceptance. See ../mvp-verified-speech-result.json.

## Privacy, licensing and incident

No real recording, source refresh/selection, account configuration or provider call was performed by this correction. ffmpeg is not bundled; local GPL encoder distribution is a separate decision. The historical unintended recording-launch incident remains disclosed in RECORDING-INCIDENT.md. Later no-device checks do not erase it.

## Evidence and lifecycle diagnostics

Receipts/logs: D:\OpenClicky\.artifacts\unified-20261028. Current candidate: mvp-fixed-stage-result.json and native-gui-followup-fixed.json. Source report: docs/verification/2026-10-28-native-followup-fix.md in the isolated worktree.

Explicit isolated runs write best-effort unified-mvp-lifetime-<PID>.json beside the executable, with run/PID/timestamps/phase and confirmed-shutdown state only. A missing final marker is not a successful shutdown. No user content or credentials are recorded.

OpenClicky.App.dll SHA256: D6FA3CA7693687FF842CF6AA3B62CAD501317C1F07D903AEC3BE6C1FD331E192.

An optional image-only capture from the hidden WebView timed out. That extra test path was reverted; it is not evidence of a successful screenshot or visible-desktop check. The stable source remains committed as 1031384.

Final native receipt: ../mvp-fixed-visible-toolbar-result.json. Native-only follow-up is complete and its monitor has been deleted. Independent source review remains unavailable; real hardware/providers/accounts, physical multi-monitor transitions and full 99-feature acceptance remain unverified. Product code revision remains 1031384; no rebuild is needed for documentation-only reconciliation.
