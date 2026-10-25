using OpenClicky.Core.Shell;

namespace OpenClicky.Core.Settings;

/// <summary>Fixed values and caps shared by the patch validator and the view builder.</summary>
public static class SettingsCatalog
{
    public static readonly IReadOnlyList<double> Speeds = [0.5, 0.75, 1, 1.25, 1.5];
    public static readonly IReadOnlyList<string> Accents = ["#3380FF", "#FF4C4C", "#D4A017", "#26C281"];
    public static readonly IReadOnlyList<string> OpenAiVoices = Speech.VoiceCatalog.OpenAiIds;
    public const string WindowsDefaultVoice = "windows-default";
    public const string DefaultProxyUrl = "http://127.0.0.1:8787";
    public const int MaxDictionaryWords = 50, MaxWordLength = 60, MaxDenyListChars = 2_000, MaxIdLength = 160, MaxVoiceLength = 80;

    public static readonly IReadOnlyList<DictationLanguage> Languages =
    [
        new("en", "English", "English"), new("es", "Spanish", "Español"), new("fr", "French", "Français"),
        new("de", "German", "Deutsch"), new("it", "Italian", "Italiano"), new("pt", "Portuguese", "Português"),
        new("nl", "Dutch", "Nederlands"), new("sv", "Swedish", "Svenska"), new("pl", "Polish", "Polski"),
        new("tr", "Turkish", "Türkçe"), new("ru", "Russian", "Русский"), new("uk", "Ukrainian", "Українська"),
        new("ar", "Arabic", "العربية"), new("hi", "Hindi", "हिन्दी"), new("ja", "Japanese", "日本語"),
        new("ko", "Korean", "한국어"), new("zh", "Chinese", "中文"), new("cs", "Czech", "Čeština"),
        new("da", "Danish", "Dansk"), new("fi", "Finnish", "Suomi"), new("no", "Norwegian", "Norsk"),
        new("el", "Greek", "Ελληνικά"), new("he", "Hebrew", "עברית"), new("hu", "Hungarian", "Magyar"),
        new("ro", "Romanian", "Română"), new("sk", "Slovak", "Slovenčina"), new("bg", "Bulgarian", "Български"),
        new("hr", "Croatian", "Hrvatski"), new("sr", "Serbian", "Српски"), new("sl", "Slovenian", "Slovenščina"),
        new("lt", "Lithuanian", "Lietuvių"), new("lv", "Latvian", "Latviešu"), new("et", "Estonian", "Eesti"),
        new("ca", "Catalan", "Català"), new("gl", "Galician", "Galego"), new("id", "Indonesian", "Bahasa Indonesia"),
        new("ms", "Malay", "Bahasa Melayu"), new("vi", "Vietnamese", "Tiếng Việt"), new("th", "Thai", "ไทย"),
        new("tl", "Tagalog", "Tagalog"), new("fa", "Persian", "فارسی"), new("ur", "Urdu", "اردو"),
        new("bn", "Bengali", "বাংলা"), new("ta", "Tamil", "தமிழ்"), new("te", "Telugu", "తెలుగు"),
        new("mr", "Marathi", "मराठी"), new("ml", "Malayalam", "മലയാളം"), new("sw", "Swahili", "Kiswahili"),
        new("af", "Afrikaans", "Afrikaans"), new("cy", "Welsh", "Cymraeg"),
    ];

    /// <summary>The offered accent matching <paramref name="colour"/> (any case), else the default.</summary>
    public static string SnapAccent(string? colour) =>
        Accents.FirstOrDefault(a => a.Equals(colour, StringComparison.OrdinalIgnoreCase)) ?? ShellStateBuilder.DefaultAccent;

    public static double SnapSpeed(double speed) => Speeds.FirstOrDefault(s => Math.Abs(s - speed) < 1e-9, 1);

    /// <summary>Trimmed, control-character-free words, de-duplicated ignoring case, each cut to 60 chars, at most 50.</summary>
    public static List<string> CleanDictionary(IEnumerable<string?>? words)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var raw in (words ?? []).Take(1_000))
        {
            var word = Cut(raw?.Trim() ?? "", MaxWordLength).TrimEnd();
            if (word.Length == 0 || word.Any(char.IsControl) || !seen.Add(word)) continue;
            result.Add(word);
            if (result.Count == MaxDictionaryWords) break;
        }
        return result;
    }

    public static string CleanDenyList(string? text) => Cut(text?.Trim() ?? "", MaxDenyListChars);

    /// <summary>Cuts to <paramref name="max"/> chars without leaving half a surrogate pair.</summary>
    public static string Cut(string value, int max)
    {
        if (value.Length <= max) return value;
        return char.IsHighSurrogate(value[max - 1]) ? value[..(max - 1)] : value[..max];
    }

    /// <summary>
    /// The http/https loopback URL (no path trailing slash), or null when it is anything else. Credentials
    /// (user:pass@) are rejected unless <paramref name="stripUserInfo"/>, in which case they are removed.
    /// </summary>
    public static string? LoopbackUrl(string? text, bool stripUserInfo = false)
    {
        var t = text?.Trim();
        if (string.IsNullOrEmpty(t) || t.Length > 200 || !Uri.TryCreate(t, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme is not ("http" or "https") || !uri.IsLoopback) return null;
        if (uri.UserInfo.Length > 0 && !stripUserInfo) return null;
        var host = uri.HostNameType == UriHostNameType.IPv6 ? uri.Host : uri.IdnHost;
        var port = uri.IsDefaultPort ? "" : ":" + uri.Port;
        return $"{uri.Scheme}://{host}{port}{uri.AbsolutePath}".TrimEnd('/');
    }
}
