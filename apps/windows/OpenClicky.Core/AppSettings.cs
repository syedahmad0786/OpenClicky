using OpenClicky.Core.Settings;

namespace OpenClicky.Core;

public sealed class AppSettings
{
    public HotkeyChord PushToTalk { get; set; } = HotkeyChord.Default;

    /// <summary>Optional localhost proxy, e.g. http://127.0.0.1:8787. Empty = call OpenRouter directly.</summary>
    public string LocalProxyBaseUrl { get; set; } = "http://127.0.0.1:8787";

    public bool UseLocalProxy { get; set; }

    /// <summary>Proactive cards, all off by default. A null from hand-edited JSON is treated as defaults by every reader.</summary>
    public OpenClicky.Core.Proactive.ProactiveSettings Proactive { get; set; } = new();

    /// <summary>Which provider Talk/Agent call. OpenRouter remains an explicit alternative.</summary>
    public AuthProvider AuthProvider { get; set; } = AuthProvider.ChatGPT;

    public string? OpenRouterApiKey { get; set; }

    public string OpenRouterModel { get; set; } = "openai/gpt-4o-mini";

    public const string DefaultAnthropicModel = "claude-sonnet-5-5", DefaultXaiModel = "grok-4";

    /// <summary>Claude model id (Anthropic Messages API). The key lives only in <see cref="SecretNames.AnthropicApiKey"/>.</summary>
    public string AnthropicModel { get; set; } = DefaultAnthropicModel;

    /// <summary>Grok model id (xAI API). The key lives only in <see cref="SecretNames.XaiApiKey"/>.</summary>
    public string XaiModel { get; set; } = DefaultXaiModel;

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

    /// <summary>Shell accent, #RRGGBB. Alternatives offered: #FF4C4C, #D4A017, #26C281.</summary>
    public string AccentColor { get; set; } = "#3380FF";

    /// <summary>Name shown in the shell; empty falls back to the ChatGPT account or Windows user name.</summary>
    public string? DisplayName { get; set; }

    public bool ShowInTaskbar { get; set; }
    public bool ShowInScreenRecordings { get; set; } = true;
    public bool LaunchAtLogin { get; set; }

    /// <summary>While paused the talk hotkey is ignored.</summary>
    public bool Paused { get; set; }

    // Settings page (sub-project S). Old settings.json files lack these and get the defaults.

    /// <summary>"windows-default", an installed Windows voice id, or an OpenAI voice name.</summary>
    public string VoiceId { get; set; } = SettingsCatalog.WindowsDefaultVoice;
    /// <summary>0.5, 0.75, 1, 1.25 or 1.5.</summary>
    public double SpeechSpeed { get; set; } = 1;
    public bool Captions { get; set; } = true;
    public string? OutputDeviceId { get; set; }
    public bool DictationAutoDetect { get; set; } = true;
    /// <summary>Tidy dictated text (punctuation, filler words) with the model provider.</summary>
    public bool DictationCleanup { get; set; } = true;
    /// <summary>English talk and dictation decode on the larger small.en model once it is downloaded (fewer misheard words, slower).</summary>
    public bool AccurateSpeech { get; set; }
    /// <summary>The one-time "Tidy up with AI is on" card has been shown.</summary>
    public bool DictationCleanupNoticeShown { get; set; }
    public string DictationLanguage { get; set; } = "en";
    /// <summary>At most 50 words of 60 characters, de-duplicated ignoring case.</summary>
    public List<string> DictionaryWords { get; set; } = [];
    public HotkeyBinding TalkHotkey { get; set; } = HotkeyBinding.DefaultTalk;
    public HotkeyBinding TextHotkey { get; set; } = HotkeyBinding.DefaultText;
    public HotkeyBinding DictateHotkey { get; set; } = HotkeyBinding.DefaultDictate;
    public HotkeyBinding HandsFreeHotkey { get; set; } = HotkeyBinding.DefaultHandsFree;
    /// <summary>Shortcut settings schema; 0 when absent. <see cref="HotkeyMigration"/> moves it to 2.</summary>
    public int ShortcutsVersion { get; set; }
    public bool CursorHidden { get; set; }
    public bool ScreenHighlight { get; set; } = true;
    public bool ShakeToDetach { get; set; } = true;
    /// <summary>Folder agents work in; empty = the app default.</summary>
    public string? AgentFolder { get; set; }
    /// <summary>The Obsidian vault folder (contains .obsidian) agents may read and write notes in; null = no vault connected.</summary>
    public string? ObsidianVaultPath { get; set; }
    public bool SpeakJobAnnouncements { get; set; } = true;
    public bool CursorJobUpdates { get; set; } = true;

    public string ResolveVisionModel() => AuthProvider switch
    {
        AuthProvider.ChatGPT => ChatGptModel.Trim(),
        AuthProvider.Anthropic => string.IsNullOrWhiteSpace(AnthropicModel) ? DefaultAnthropicModel : AnthropicModel.Trim(),
        AuthProvider.Xai => string.IsNullOrWhiteSpace(XaiModel) ? DefaultXaiModel : XaiModel.Trim(),
        _ => string.IsNullOrWhiteSpace(OpenRouterModel) ? "openai/gpt-4o-mini" : OpenRouterModel.Trim(),
    };

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
