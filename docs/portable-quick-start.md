# Welcome to OpenClicky 0.6.0

Open **OpenClicky.exe** from the complete extracted folder on Windows 11 x64. Keep its runtime folders beside it. This is an unsigned preview.

## Connect and talk

1. Open **Settings → Connection → ChatGPT plan → Continue with ChatGPT**. Complete the official sign-in yourself, enable plan usage, and choose an available model.
2. In **Settings → Voice & shortcut**, select your actual microphone, save it, and use **Test microphone**. Download the local English Whisper model once.
3. Left-click the small companion once, or click **Start conversation**. Speak, pause, and speak again after its reply. Click again to stop. Hold **Ctrl+Alt** for a single hold-to-talk request.
4. Type in the same Chat if voice is unavailable. Screen requests give you three seconds to bring the target app forward. If OpenClicky still has focus, bring the intended app forward and retry when prompted; its own window is not a screen target. Local note and task-control commands respond immediately. Self-contained chart/calculator/diagram/3D requests stay in the workspace without capturing a screen.

If the saved microphone is disconnected, reconnect it or explicitly choose another. OpenClicky does not silently change your device. Live transcription and replies have measurable delays; the three-second response target is not yet met on this laptop.

## Work in one place

**“Open Calculator”** or a request in **Do a task · Agent** runs through the managed OpenWork runtime. OpenCode is an internal OpenWork dependency; there is no separate engine workspace or provider sign-in. **Tasks** shows progress, approvals and results. Use **pause task**, **resume task**, **stop task**, or **change task:** followed by instructions. Taking over pauses input; resuming checks the destination again.

**Approve each action** is the default. Preview performs no input. Auto asks for the plan and confirms actions its rules identify as consequential or uncertain. Full access requires explicit warning acceptance. These controls are not an operating-system sandbox; Stop cannot undo completed input.

**“Show me how to…”** starts a guide. Perform the indicated action, then choose **Next** to inspect the result. **Back** reviews without undoing; **Recheck** uses a fresh view. The companion can mark controls with labels, circles, rectangles, arrows, polygons, freehand strokes and curves.

**Dictate into app:** click it, then click the intended writable field. Recording starts automatically. Speak and pause; each finished phrase is typed into that same field without pressing Send or Enter, and words that cannot be typed or confirmed there go to the clipboard with a notice. If the destination changes, insertion stops and the draft is preserved. Review/edit that preserved draft before explicitly choosing **Insert into original app**. This is a fallback within the same flow. With **Tidy up with AI** on (Settings → Dictation, on by default) and a model provider configured, each phrase's punctuation and filler words are tidied by your model provider before it is typed (see below); the classic window without the notch types the raw words.

**Clipboard shelf** persists exact URLs, numbers and multiline text. Start/Pause collection is explicit. You can also add text manually, edit, pin, reorder, search, copy all, or choose a destination and paste items in order. OpenClicky's own clipboard writes are ignored. Existing clips stay until deleted; a full shelf refuses new clips instead of evicting old ones.

**Memory** shows editable preferences and facts with their conversation sources. Limited automatic learning recognizes explicit preferences and supported facts; it does not rewrite the app. **Notes** stores longer text. Task memory search can retrieve saved context with source references.

Ask for a **chart, connected diagram, calculator or 3D explanation** in Chat. Use its controls, save it, and request follow-up changes. Visuals stay attached to the conversation. The packaged renderer uses constrained data and an isolated, scriptless 3D frame.

**Offer help when I seem stuck** uses local activity patterns. A quiet offer waits for your acceptance before task-specific screen inspection. Dismiss or snooze it; Escape pauses awareness.

The optional **Chrome bridge** works with existing signed-in tabs on sites you choose. Connections provides local setup; Chrome extension installation and site permission review are manual. Gmail/Slack API connectors, Composio/Nango, recurring routines and multiple companions remain phase two. ChatGPT sign-in does not grant Gmail or Slack access.

## What leaves your PC

Local Whisper recognition and Windows voices stay on the PC; raw audio is discarded. With **Tidy up with AI** on (Settings → Dictation) and a model provider configured, each dictated phrase's transcript, the app name, the site host, up to 2,000 characters either side of the cursor, the selected text and your dictionary words go to your model provider to tidy it; the audio stays on the PC. Password fields, OpenClicky's own windows and deny-listed apps are never read. With an OpenAI voice selected in Settings → Voice, the text of each reply sentence (and the voice preview line) is sent to api.openai.com to be spoken; the audio comes back and is not kept. Explicit screen questions send recognized/typed text and every connected display (up to four, the one with your cursor first, without OpenClicky's own windows), or only the region you select, to ChatGPT. A display showing a protected window is left out. With Screen highlight on (Settings → Cursor), what you draw while holding talk is painted into that question's screenshot, and the text of the controls under your marks (never password fields or protected apps) goes with it. Tasks inspect the windows/tabs needed for their work. Requested saved-memory context can also go to ChatGPT. Ambient local activity does not continuously upload screenshots or record room audio.

Conversations, notes, clips and source-linked memory use local storage in `%APPDATA%\OpenClicky`; credentials use the protected store. The history preference remains respected. The retained legacy JSON files are recovery originals. Existing optional OpenRouter cloud transcription sends audio only if explicitly selected.

## Reopen and exit

Closing Home keeps the companion in the tray. Check **^ Show hidden icons** near the clock, double-click the icon, or right-click the companion → **Open workspace**. Launching the EXE again reopens the existing instance. **Tools → Quit** exits; **Stop conversation** ends listening without quitting.

The [acceptance ledger](https://github.com/syedahmad0786/OpenClicky) is also available in the source repository at `docs/phase-one-implementation.md`. Physical microphone/keyboard, extension installation, mixed-DPI and clean-machine checks remain separate from automated and native fixture proof. The source checkout may contain changes newer than the public repository.

MIT licensed. See LICENSE and THIRD-PARTY-NOTICES.md in this folder.
