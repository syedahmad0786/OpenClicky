# OpenClicky

<img src="assets/openclicky.png" alt="OpenClicky green pointer companion" width="112" />

One open-source Windows companion for conversation, screen guidance, approved desktop work, dictation, notes and interactive visuals. MIT licensed.

Inspired by [HeyClicky](https://www.heyclicky.com/) and the public MIT project [farzaa/clicky](https://github.com/farzaa/clicky). OpenClicky has its own branding and Windows implementation.

## Try the 0.6.0 preview

Windows 11 x64. Extract the complete portable folder and open **OpenClicky.exe**. The required local runtimes are bundled; keep their folders beside the EXE.

- Connect through **Settings → Connection → ChatGPT plan → Continue with ChatGPT**. Complete official sign-in yourself and choose a model available to your account.
- Select and test your microphone under **Voice & shortcut**, then download local English Whisper.
- Click the companion once for continuous conversation. Click again to stop; hold **Ctrl+Alt** for one request (double-tap **Ctrl** to type, hold **Ctrl+Win** to dictate).
- Ask **“Open Calculator”**, **“Show me how to…”**, or request a chart in the same Chat.
- Use **Dictate into app**, **Clipboard shelf**, **Memory**, **Notes** and **Tasks** from that workspace.

[Quick start](docs/portable-quick-start.md) explains the exact controls and data sharing. [Physical acceptance](docs/manual-acceptance-2026-10-12.md) lists microphone and keyboard checks.

## Current status

**0.6.0 is an unsigned implementation preview; phase-one acceptance is still in progress.** The [implementation ledger](docs/phase-one-implementation.md) distinguishes real AI, native fixture, automated, physical and packaging evidence. Earlier reports remain historical evidence rather than current release certification.

| Capability | Current evidence and limit |
| --- | --- |
| OpenWork tasks | Real ChatGPT → OpenWork → approved sandbox click/type and Calculator launch passed with independent destination inspection. Memory-only source search and declined-task Stop passed. OpenWork is the workflow foundation; OpenCode is its internal dependency. No separate provider login is required. |
| Unified chat and visuals | Packaged React/WebView2, CopilotKit v2 and AG-UI streaming. Real ChatGPT chart, connected diagram, calculator and rotatable cube passed bounded native checks; saved edits survived app restart. Self-contained visual creation keeps Home open without capturing a screen. |
| Conversation and voice | Continuous listening, streaming text/sentence speech, local Whisper warmup/partial reuse and conditional Windows echo-cancelled interruption are built. Current 0.6 physical acceptance remains open. |
| Dictation | Finalized raw words insert into the selected writable field on pause. Exact native insertion and refusal after focus changes passed; physical speech-to-field acceptance remains open. No Send/submit or automatic AI rewrite. |
| Guidance | Step-by-step walkthroughs, Next/Back/Recheck/Stop, selected regions and seven progressive drawing shapes. A real three-step task checked progress, preserved existing text and re-grounded after moving its window. All shapes have native fixture proof; broader app and mixed-DPI checks remain separate. |
| Memory and notes | Source-linked editable local facts, bounded automatic learning, local notes and read-only task memory search. Deletion suppression and transactional SQLite import have focused coverage. |
| Clipboard shelf | Persistent exact text, URLs and numbers; edit/pin/reorder/search and sequential insertion. Exact multiline destination proof passed. Saved collection preference is preserved. |
| Awareness | Local repeated-activity signals, quiet opt-in offers, five-minute cooldown, dismissal and snoozing. No continuous screen uploading. |
| Browser | Optional scoped Chrome extension and native messaging are implemented. Installing it and verifying existing signed-in tabs remain manual acceptance items. |
| Distribution | Version-aligned EXE, ZIP and unsigned MSIX previews. Final packaged-launch, signing and clean-machine acceptance are tracked in the ledger. |

Performance is measured separately for capture, transcription, model response and speech. Recent real model first-text times exceeded the three-second warm-response target; another routing provider has not been added without evidence that routing is the bottleneck.

**Phase two (built, not yet natively accepted):** the **Work** tab adds named companions with their own notes, saved background jobs that save files into their own `output/` folder, and daily/weekday/interval schedules. **Connections** accepts any MCP server address (your own or a provider such as Composio); read-only tools run directly and others ask first. See the [phase-two spec and gap list](docs/heyclicky-behavior-spec-2026-10-24.md) and the [feature plan](docs/feature-plan.md). ChatGPT sign-in does not grant access to Gmail/Slack APIs; each connector needs its own consent.

## Privacy and control

- Screen questions share every connected display (up to four, the one with your cursor first) with OpenClicky's own windows left out, or only the area you select, plus request text, with ChatGPT. A display showing a protected window (Settings → Access) is left out. Tasks inspect relevant windows/tabs and may retrieve requested saved context. Raw audio is transcribed locally and discarded.
- Ambient awareness starts with local activity signals and asks before inspecting a task. Exclusions are applied before supported inspection/capture paths. Window titles are convenience guards, not a security boundary.
- ChatGPT credentials stay in the protected store. The private local inference adapter gives the managed OpenWork engine short-lived local authentication, not stored provider credentials.
- Conversations, notes, clips and artifact references persist locally in SQLite; original JSON imports remain for recovery. The history preference is respected. Memory is editable and source-linked; forgetting suppresses recreation from its retained source.
- **Confirm each** remains the default. Preview disables mutations. Auto confirms the plan and actions its rules classify as consequential/uncertain. Full access requires explicit warning acceptance. A running task keeps its initial access snapshot. Stop cannot undo completed input.
- Generated visuals use constrained data and isolated rendering. Desktop actions still pass through the host broker. The task profile cannot use unrestricted shell tools.
- Existing optional OpenRouter settings are retained for compatibility; cloud audio is sent only when explicitly selected.
- Windows voices speak replies on the PC. With an OpenAI voice selected (Settings → Voice), the text of each reply sentence, and the voice preview line, is sent to api.openai.com with your OpenAI key to be spoken; the audio comes back and is not kept.

See [agent safety](docs/agent-safety.md), [architecture](docs/architecture.md) and [video requirements](docs/video-acceptance-matrix.md). The video review is sampled visual evidence, not a claim of frame-by-frame inspection or complete feature parity.

## Build from source

Use .NET 8 SDK or newer on Windows and Node/pnpm for the embedded workspace.

```powershell
dotnet build OpenClicky.sln -c Release
dotnet test OpenClicky.sln -c Release --no-build
pnpm --dir apps/companion-workspace install --frozen-lockfile
pnpm --dir apps/companion-workspace test
pnpm --dir apps/companion-workspace build
powershell -ExecutionPolicy Bypass -File scripts/setup-openwork.ps1
dotnet run --project apps/windows/OpenClicky.App -c Release
```

Publish the portable app and optional unsigned MSIX:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish-exe.ps1
powershell -ExecutionPolicy Bypass -File scripts/pack-msix.ps1
```

Outputs are under `artifacts/phase-one-preview/`. [Packaging](docs/packaging.md) explains runtime verification, licenses and distribution. A clean-machine installation and signing are not implied by a successful build.

The latest test candidate is isolated under `artifacts/phase-one-screen-guard/`; its native refusal check and matching archives are pending. The previous `artifacts/phase-one-ready/` archives retain bounded visual acceptance but do not include that guard. Work is checkpointed in the [usage-pause handoff](docs/checkpoint-2026-10-12-usage-pause.md). The [implementation ledger](docs/phase-one-implementation.md) records hashes and evidence. Neither candidate is a final release.

Dedicated smoke executables use their own disposable destinations. Automatic approval is limited to the test harness. Offline fixtures remain visibly labeled; normal launches ignore stale debug transcript overrides. Inspect the actual destination for desktop acceptance.

## License

[MIT](LICENSE). See [third-party notices](THIRD-PARTY-NOTICES.md) and the packaged dependency license inventory. No HeyClicky logo, commercial binary or marketing video is included in the release.
