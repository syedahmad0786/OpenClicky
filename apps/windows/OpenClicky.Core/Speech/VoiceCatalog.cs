namespace OpenClicky.Core.Speech;

/// <summary>The OpenAI text-to-speech voices OpenClicky offers, each with a short description in our own words.</summary>
public static class VoiceCatalog
{
    public static readonly IReadOnlyList<(string Id, string Descriptor)> OpenAi =
    [
        ("alloy", "Even and neutral"),
        ("ash", "Clear and direct"),
        ("ballad", "Soft and melodic"),
        ("cedar", "Deep and grounded"),
        ("coral", "Bright and friendly"),
        ("echo", "Calm and measured"),
        ("marin", "Natural and relaxed"),
        ("sage", "Gentle and thoughtful"),
        ("shimmer", "Light and upbeat"),
        ("verse", "Expressive and lively"),
    ];

    public static readonly IReadOnlyList<string> OpenAiIds = OpenAi.Select(v => v.Id).ToList();

    /// <summary>Exact, lower-case ids only (the ids the settings page sends).</summary>
    public static bool IsOpenAi(string? id) => id is not null && OpenAiIds.Contains(id);

    /// <summary>The voice a job announcement uses: the selected one, except that with "Read replies aloud" off an OpenAI voice
    /// becomes the local Windows voice, so an announcement never sends its line off the machine then.</summary>
    public static string ForAnnouncement(string voiceId, bool readRepliesAloud) =>
        readRepliesAloud || !IsOpenAi(voiceId) ? voiceId : Settings.SettingsCatalog.WindowsDefaultVoice;

    public static string Descriptor(string id) => OpenAi.FirstOrDefault(v => v.Id == id).Descriptor ?? "OpenAI voice";
}
