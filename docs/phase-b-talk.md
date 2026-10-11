# Phase B — Talk path

## Flow

1. Hold remappable PTT (default Win+Alt+Space) → mic records WAV (NAudio).  
2. Release → overlay processing → STT (OpenRouter `/audio/transcriptions`) **or** Settings debug transcript.  
3. Capture monitors to JPEG in memory.  
4. OpenRouter vision chat with OpenClicky system prompt + history (last 10).  
5. Parse `[POINT:…]`, map to virtual screen, overlay bubble + optional point.  
6. Speak via local Windows SAPI TTS (no cloud TTS key required).

## Settings (`%AppData%\OpenClicky\settings.json`)

- OpenRouter API key  
- Model id (default `openai/gpt-4o-mini`)  
- Optional local proxy  
- Debug transcript override  

## Verify

```powershell
dotnet test tests/OpenClicky.Core.Tests
dotnet run --project apps/windows/OpenClicky.App
```

Use debug transcript `what do you see on my screen?` for a no-mic smoke test.
