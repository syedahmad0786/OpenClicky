namespace OpenClicky.Core.Dictation;

/// <summary>A downloadable Whisper model file, verified by exact size and SHA-256.</summary>
public sealed record WhisperModelInfo(string Id, string FileName, long Size, string Sha256, string Url, bool Multilingual);

/// <summary>Which model to run and the language to decode in: a code, or <c>"auto"</c> to detect it.</summary>
public sealed record WhisperDecodePlan(WhisperModelInfo Model, string Language);

/// <summary>Per-transcription decoder options: the language (a code or <c>"auto"</c>) and an optional vocabulary prompt.</summary>
public sealed record WhisperDecodeOptions(string Language, string? Prompt);

public static class WhisperModels
{
    public static readonly WhisperModelInfo English = new("english", "ggml-base.en.bin", 147964211,
        "a03779c86df3323075f5e796cb2ce5029f00ec8869eee3fdfb897afe36c6d002",
        "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin", Multilingual: false);

    public static readonly WhisperModelInfo Multilingual = new("multilingual", "ggml-base.bin", 147951465,
        "60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe",
        "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin", Multilingual: true);
}

public static class WhisperModelChoice
{
    /// <summary>
    /// The model and language for these settings and installed models. English prefers the English model; auto-detect and
    /// other languages need the multilingual one and fall back to English on the English model. Null when neither is installed.
    /// </summary>
    public static WhisperDecodePlan? Resolve(bool autoDetect, string language, bool englishReady, bool multilingualReady)
    {
        if (!autoDetect && language == "en")
            return englishReady ? new(WhisperModels.English, "en") : multilingualReady ? new(WhisperModels.Multilingual, "en") : null;
        if (multilingualReady) return new(WhisperModels.Multilingual, autoDetect ? "auto" : language);
        return englishReady ? new(WhisperModels.English, "en") : null;
    }
}

public static class WhisperPrompt
{
    public const int MaxLength = 600;

    /// <summary>The dictionary as a Whisper initial prompt: words joined by ", ", whole words only, at most 600 characters.</summary>
    /// <summary>
    /// Whisper can "hear" its own prompt in near-silence. An echo is a transcript that, trimmed (with any closing full stop or
    /// comma) and ignoring case, is the prompt or its start up to a word boundary.
    /// </summary>
    public static bool IsEcho(string transcript, string? prompt)
    {
        var t = (transcript ?? "").Trim().TrimEnd('.', ',', '。').TrimEnd();
        return prompt is not null && t.Length > 0 && prompt.StartsWith(t, StringComparison.OrdinalIgnoreCase)
            && (t.Length == prompt.Length || prompt[t.Length] == ',');
    }

    public static string? FromDictionary(IReadOnlyList<string> words)
    {
        if (words.Count == 0) return null;
        var prompt = string.Join(", ", words);
        if (prompt.Length <= MaxLength) return prompt;
        var cut = prompt.LastIndexOf(", ", MaxLength + 1, StringComparison.Ordinal); // a separator starting at 600 still keeps 600
        return cut > 0 ? prompt[..cut] : null; // a single word over 600 chars cannot happen (words are capped at 60)
    }
}
