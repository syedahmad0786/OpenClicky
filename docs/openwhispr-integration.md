# OpenWhispr ideas in OpenClicky

Reviewed 11 October 2026 at OpenWhispr commit `5f9a88b0481a066c147bfd2c38ea1d9acae502ee`. The reference checkout is under ignored research artifacts; none of its scripts were executed.

OpenWhispr is a useful MIT-licensed reference for a voice-first desktop product. Its [README](https://github.com/OpenWhispr/openwhispr) describes local and cloud transcription, dictation, notes, meeting transcription and an assistant. This does not establish acceptance on this Windows machine.

## Product direction

Keep one OpenClicky window, pointer companion, voice shortcut and connection. Add **Ask**, **Dictate** and **Notes** as separate destinations for the same voice input. ChatGPT sign-in powers reasoning; local speech recognition supplies words. Do not automatically turn dictation into an Agent command or send a private note to a provider.

The current .NET Windows app uses whisper.cpp through Whisper.net. OpenWhispr's Electron/React screens and audio code cannot be dropped directly into WinForms. The shared engine and interaction ideas fit our app; a full Electron host, second process and separate login/settings store would add substantial maintenance. No OpenWhispr UI or executable is bundled in 0.3.1.

| Reference pattern | OpenClicky status |
| --- | --- |
| Explicit microphone and stable device identity | Built: native endpoint selector, refresh, saved ID and input-level test; unavailable saved inputs fail instead of silently switching |
| Offline transcription and managed model download | Built: English Whisper base.en CPU, bounded download, pinned size/SHA-256, live previews and final recognition; Windows compatibility fallback before download |
| Floating recording feedback | Built: transcript preview near the pointer and in Home; chosen microphone and engine visible in Home |
| Preview/edit before insertion | Next: Dictate mode, editable draft, explicitly inspected destination and approval; reuse the existing input executor |
| Preserve original words when AI cleanup fails | Planned: separate original and cleaned drafts, optional ChatGPT formatting, restore original and retry; cleanup cannot initiate desktop actions |
| Notes and folders | Built in 0.4.0: local titled notes, editable content, search and Markdown export. Folders and optional AI summaries retaining original text remain pending |
| Custom vocabulary and multilingual input | Planned after English microphone acceptance; keep this release's English-only model explicit |
| Meetings, speakers and imports | Later: visible recording state, explicit recording controls, streaming storage and retention policy before long recordings; no implied meeting capture |

Relevant source references: [microphone UI](https://github.com/OpenWhispr/openwhispr/blob/5f9a88b0481a066c147bfd2c38ea1d9acae502ee/src/components/ui/MicrophoneSettings.tsx), [device matching](https://github.com/OpenWhispr/openwhispr/blob/5f9a88b0481a066c147bfd2c38ea1d9acae502ee/src/helpers/micDeviceSelection.js), [cleanup validation](https://github.com/OpenWhispr/openwhispr/blob/5f9a88b0481a066c147bfd2c38ea1d9acae502ee/src/utils/cleanupOutput.ts), [license](https://github.com/OpenWhispr/openwhispr/blob/5f9a88b0481a066c147bfd2c38ea1d9acae502ee/LICENSE).

## Acceptance before expansion

AI Wireless Microphone was selected, saved and visibly retained after restart. The user confirmed **Accurate now** for the first real phrase's preview and final text. Native fixtures also passed loading, transcription, silence handling, resampling and cancellation checks. Broader accent/noise accuracy and the physical shortcut remain open. The next implementation slices are Dictate and Notes.

0.3.1 introduced the English model and voice requests under one minute; these limits remain in 0.4.0. CPU inference adds a few seconds to previews. Audio remains in memory and is discarded; the public model download sends no audio. Ask shares connected screen images, while an explicit region request shares only the selected pixels. Local notes are built in 0.4.0; dictation into other apps and meetings remain unbuilt.
