# Windows preview verification

Local run: 11 October 2026 on Windows 11 x64. Working branch: `codex/windows-product`, based on `main` at `c84cef9`. The original Cursor checkout was preserved. This record distinguishes implementation, automated checks, and actual desktop observation.

## Passed

| Check | Actual evidence |
| --- | --- |
| Release solution build | Zero warnings and errors |
| Unit tests | 128 passed, zero failed or skipped after ChatGPT integration changes |
| ChatGPT protocol checks | Signed/forged/unsigned identity tokens, nonce/audience/issuer/expiry rejection, JWKS failure, exact loopback callback, account binding, rotating refresh/sign-out serialization, permission loss, ordered model catalog, image/history request format, and interrupted/failed stream rejection tested with isolated fixtures |
| ChatGPT connection screen | Published app visually inspected; ChatGPT selected and saved, local speech enabled, Continue with ChatGPT visible; human authentication still pending |
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

The full-app test used the built-in offline planner and normal human approval dialogs. The external test executable used automatic approval only inside its own test process. Neither proves a live model can plan a general desktop task successfully. The generated WAV test proves local speech integration, not the physical microphone or recognition accuracy for a person's voice.

The separate Notepad check used the opt-in offline Agent fixture with the normal production confirmation/focus-restoration path. It opened an owned blank test file, selected that editor during Home's countdown, and approved a single `type_text: hello` action. The screenshot showed `hello` and five characters; saving the file confirmed its exact contents. Other Notepad tabs were preserved. This verifies the handover's specific external-editor input requirement without treating an input log as proof.

## Fixes motivated by observed failures

- Approval could lose the selected window/control and type into the wrong target. Focus is now restored and verified; failure stops the action instead of falling back to the desktop.
- A test could accidentally select another sandbox with the same title and accept `hellohello`. It now scopes to its own process and requires exact `hello`.
- The legacy offline Agent plan launched Run/Notepad and claimed completion without inspecting the editor. It now types only the test word into an already selected field and explicitly asks for visual verification.
- The low-level keyboard callback read modifier state before Windows updated it. It now tracks physical key events, ignores injected agent events, and reserves Escape for cancellation.
- Saved debug transcript fixtures could replace real voice input on later launches. Normal launches now ignore that override.
- UI scaling clipped navigation and settings. Layout construction now uses a consistent DPI baseline and completes scaling after controls are built.
- Idle overlay animation continuously repainted the desktop; the timer now runs only while the overlay is visible. The transparency key uses a neutral dark edge rather than a purple fringe.

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

1. **Connect ChatGPT yourself.** No signed-in ChatGPT session was available. Open Settings → Connection → ChatGPT plan → Continue with ChatGPT, finish the official browser sign-in, and enable plan usage. Do not paste credentials into chat. Close or cover private content on every connected monitor before a live test.
2. **Live Ask and POINT.** Put harmless sample text in a test window, ask about a visible control, and verify the reply and pointing match the actual UI. Record the selected provider/model and observed response without logging secrets.
3. **Live Agent.** Use the standalone sandbox. Select it during the three-second countdown, request a button click and `hello`, approve each action, then inspect the exact result. Also decline a proposed action and cancel a pending request.
4. **Physical voice.** Hold the default shortcut, speak a request, release, and inspect the recognized text. Repeat with a remapped shortcut. Test Escape while listening and during speech.
5. **Distribution acceptance.** Signed installation, clean-machine launch, MSIX installation, and upgrade behavior remain unverified. GitHub CI has not run the new local branch.

Full HeyClicky parity is outside this preview's implemented scope; the missing features are listed in [the research map](heyclicky-research.md). Do not mark the product complete or operationally accepted based only on this local record.
