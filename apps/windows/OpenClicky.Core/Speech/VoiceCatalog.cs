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

    public static string Descriptor(string id) => OpenAi.FirstOrDefault(v => v.Id == id).Descriptor ?? "OpenAI voice";
}
