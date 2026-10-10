namespace OpenClicky.Core;

public sealed class AppSettings
{
    public HotkeyChord PushToTalk { get; set; } = HotkeyChord.Default;

    /// <summary>Optional localhost proxy, e.g. http://127.0.0.1:8787. Empty = call providers directly later.</summary>
    public string LocalProxyBaseUrl { get; set; } = "http://127.0.0.1:8787";

    public string? OpenRouterApiKey { get; set; }

    public string OpenRouterModel { get; set; } = "openai/gpt-4o-mini";
}
