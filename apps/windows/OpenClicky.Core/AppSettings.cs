namespace OpenClicky.Core;

public sealed class AppSettings
{
    public HotkeyChord PushToTalk { get; set; } = HotkeyChord.Default;

    /// <summary>Optional localhost proxy, e.g. http://127.0.0.1:8787. Empty = call OpenRouter directly.</summary>
    public string LocalProxyBaseUrl { get; set; } = "http://127.0.0.1:8787";

    public bool UseLocalProxy { get; set; }

    public string? OpenRouterApiKey { get; set; }

    public string OpenRouterModel { get; set; } = "openai/gpt-4o-mini";

    /// <summary>OpenRouter model id for audio transcription (Whisper-compatible).</summary>
    public string TranscriptionModel { get; set; } = "openai/whisper-1";

    /// <summary>When set, skip STT and use this as the user utterance (debug / no-mic).</summary>
    public string? DebugTranscriptOverride { get; set; }

    /// <summary>Persist last N Talk turns (text only) under %AppData%\OpenClicky\history.json.</summary>
    public bool PersistConversationHistory { get; set; } = true;

    /// <summary>When true, skip WGC and use GDI BitBlt (debug / broken GPU drivers).</summary>
    public bool ForceGdiCapture { get; set; }
}
