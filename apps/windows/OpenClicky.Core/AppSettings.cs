namespace OpenClicky.Core;

public sealed class AppSettings
{
    public HotkeyChord PushToTalk { get; set; } = HotkeyChord.Default;

    /// <summary>Optional localhost proxy, e.g. http://127.0.0.1:8787. Empty = call OpenRouter directly.</summary>
    public string LocalProxyBaseUrl { get; set; } = "http://127.0.0.1:8787";

    public bool UseLocalProxy { get; set; }

    /// <summary>Which provider Talk/Agent call. OpenRouter remains an explicit alternative.</summary>
    public AuthProvider AuthProvider { get; set; } = AuthProvider.ChatGPT;

    public string? OpenRouterApiKey { get; set; }

    public string OpenRouterModel { get; set; } = "openai/gpt-4o-mini";

    /// <summary>OpenRouter model id for audio transcription (Whisper-compatible).</summary>
    public string TranscriptionModel { get; set; } = "openai/whisper-1";

    public bool UseWindowsSpeech { get; set; } = true;

    public string? MicrophoneDeviceId { get; set; }

    public bool SpeakReplies { get; set; } = true;
    public bool AwarenessEnabled { get; set; } = true;

    public AgentAccessMode AgentAccess { get; set; } = AgentAccessMode.ConfirmEach;

    /// <summary>
    /// Stable host id for Sign in with ChatGPT OSS registration (<c>ext_agent_host_id</c>).
    /// Not a secret — generated once per install.
    /// </summary>
    public string? ChatGptHostId { get; set; }

    /// <summary>Issued OAuth client id (<c>oaiapp_…</c>) retained across sign-out for reauthorization.</summary>
    public string? ChatGptIssuedClientId { get; set; }

    /// <summary>Signed-in account email (display only; tokens stay in Credential Manager).</summary>
    public string? ChatGptAccountEmail { get; set; }

    /// <summary>ChatGPT Responses API model slug (from <c>/v1/models</c> when signed in).</summary>
    public string ChatGptModel { get; set; } = "";
    public string ChatGptQuickModel { get; set; } = "";

    public bool ChatGptPlanWelcomeShown { get; set; }

    /// <summary>When set, skip STT and use this as the user utterance (debug / no-mic).</summary>
    public string? DebugTranscriptOverride { get; set; }

    /// <summary>Persist last N Talk turns (text only) under %AppData%\OpenClicky\history.json.</summary>
    public bool PersistConversationHistory { get; set; } = true;

    /// <summary>When true, skip WGC and use GDI BitBlt (debug / broken GPU drivers).</summary>
    public bool ForceGdiCapture { get; set; }

    /// <summary>Comma-separated extra window-title fragments that block mutating agent actions.</summary>
    public string? AgentDenyListExtra { get; set; }

    /// <summary>True after first-run tip has been shown.</summary>
    public bool FirstRunCompleted { get; set; }

    public string ResolveVisionModel() =>
        AuthProvider == AuthProvider.ChatGPT
            ? ChatGptModel.Trim()
            : (string.IsNullOrWhiteSpace(OpenRouterModel) ? "openai/gpt-4o-mini" : OpenRouterModel.Trim());

    public string ResolveTalkModel() => AuthProvider == AuthProvider.ChatGPT && !string.IsNullOrWhiteSpace(ChatGptQuickModel)
        ? ChatGptQuickModel.Trim() : ResolveVisionModel();

    public void EnsureChatGptHostId()
    {
        if (string.IsNullOrWhiteSpace(ChatGptHostId))
        {
            ChatGptHostId = $"urn:uuid:{Guid.NewGuid():D}";
        }
    }
}
