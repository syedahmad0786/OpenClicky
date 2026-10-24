using System.Text.Json;
using System.Text.RegularExpressions;
using OpenClicky.Core.Shell;

namespace OpenClicky.Core.Settings;

public enum PatchResult { Applied, Rejected, RequiresConfirmation }

/// <summary>Fixed values and caps shared by the patch validator and the view builder.</summary>
public static class SettingsCatalog
{
    public static readonly IReadOnlyList<double> Speeds = [0.5, 0.75, 1, 1.25, 1.5];
    public static readonly IReadOnlyList<string> Accents = ["#3380FF", "#FF4C4C", "#D4A017", "#26C281"];
    public static readonly IReadOnlyList<string> OpenAiVoices = ["alloy", "ash", "ballad", "cedar", "coral", "echo", "marin", "sage", "shimmer", "verse"];
    public const string WindowsDefaultVoice = "windows-default";
    public const string DefaultProxyUrl = "http://127.0.0.1:8787";
    public const int MaxDictionaryWords = 50, MaxWordLength = 40, MaxDenyListChars = 2_000, MaxIdLength = 160, MaxVoiceLength = 80;

    public static readonly IReadOnlyList<DictationLanguage> Languages =
    [
        new("en", "English", "English"), new("es", "Spanish", "Español"), new("fr", "French", "Français"),
        new("de", "German", "Deutsch"), new("it", "Italian", "Italiano"), new("pt", "Portuguese", "Português"),
        new("nl", "Dutch", "Nederlands"), new("sv", "Swedish", "Svenska"), new("pl", "Polish", "Polski"),
        new("tr", "Turkish", "Türkçe"), new("ru", "Russian", "Русский"),
        new("uk", "Ukrainian", "Українська"),
        new("ar", "Arabic", "العربية"), new("hi", "Hindi", "हिन्दी"),
        new("ja", "Japanese", "日本語"), new("ko", "Korean", "한국어"), new("zh", "Chinese", "中文"),
    ];

    public static double SnapSpeed(double speed) => Speeds.FirstOrDefault(s => Math.Abs(s - speed) < 1e-9, 1);

    /// <summary>Trimmed, control-character-free words, de-duplicated ignoring case, each cut to 40 chars, at most 50.</summary>
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

/// <summary>
/// Validates one <c>settings.patch {key, value}</c> and applies it to <see cref="AppSettings"/> in memory. Nothing is
/// written to disk here and a rejected patch never changes the settings. Lists are capped rather than rejected.
/// </summary>
public static class SettingsPatch
{
    private static readonly Regex ModelId = new(@"^[A-Za-z0-9._:/@+\-]{1,200}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    private static readonly Dictionary<string, Action<AppSettings, bool>> Bools = new()
    {
        ["voice.captions"] = (s, v) => s.Captions = v,
        ["voice.readReplies"] = (s, v) => s.SpeakReplies = v,
        ["dictation.autoDetect"] = (s, v) => s.DictationAutoDetect = v,
        ["cursor.hidden"] = (s, v) => s.CursorHidden = v,
        ["cursor.screenHighlight"] = (s, v) => s.ScreenHighlight = v,
        ["cursor.shakeToDetach"] = (s, v) => s.ShakeToDetach = v,
        ["agents.speakAnnouncements"] = (s, v) => s.SpeakJobAnnouncements = v,
        ["agents.cursorUpdates"] = (s, v) => s.CursorJobUpdates = v,
        ["access.rememberHistory"] = (s, v) => s.PersistConversationHistory = v,
        ["advanced.useLocalProxy"] = (s, v) => s.UseLocalProxy = v,
        ["advanced.forceGdiCapture"] = (s, v) => s.ForceGdiCapture = v,
    };

    private static readonly Dictionary<string, (string Name, Func<AppSettings, HotkeyBinding> Get, Action<AppSettings, HotkeyBinding> Set)> Hotkeys = new()
    {
        ["shortcuts.talk"] = ("Talk", s => s.TalkHotkey, (s, b) => s.TalkHotkey = b),
        ["shortcuts.text"] = ("Text mode", s => s.TextHotkey, (s, b) => s.TextHotkey = b),
        ["shortcuts.dictate"] = ("Dictate", s => s.DictateHotkey, (s, b) => s.DictateHotkey = b),
        ["shortcuts.handsFree"] = ("Hands-free dictation", s => s.HandsFreeHotkey, (s, b) => s.HandsFreeHotkey = b),
    };

    /// <summary>
    /// <paramref name="confirmed"/> is true only after the user accepted the native warning for Auto/Full access;
    /// without it those modes return <see cref="PatchResult.RequiresConfirmation"/> and leave the settings alone.
    /// <paramref name="error"/> is set only when the result is <see cref="PatchResult.Rejected"/>.
    /// </summary>
    public static PatchResult TryApply(AppSettings s, string key, JsonElement value, out string? error, bool confirmed = false)
    {
        try { return Apply(s, key, value, confirmed, out error); }
        catch (Exception e) when (e is InvalidOperationException or FormatException or ArgumentException or OverflowException)
        {
            return Reject("That value is not valid.", out error);
        }
    }

    private static PatchResult Apply(AppSettings s, string key, JsonElement value, bool confirmed, out string? error)
    {
        error = null;
        if (Bools.TryGetValue(key, out var setBool))
        {
            if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return Reject("Expected on or off.", out error);
            setBool(s, value.GetBoolean());
            return PatchResult.Applied;
        }

        if (Hotkeys.TryGetValue(key, out var hotkey)) return ApplyHotkey(s, hotkey.Name, value, hotkey.Set, out error);

        switch (key)
        {
            case "voice.speed":
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var speed)
                    || !SettingsCatalog.Speeds.Any(x => Math.Abs(x - speed) < 1e-9))
                    return Reject("Speed must be 0.5, 0.75, 1, 1.25 or 1.5.", out error);
                s.SpeechSpeed = SettingsCatalog.SnapSpeed(speed);
                return PatchResult.Applied;

            case "voice.voice":
                if (Text(value, 1, out var voice, out error, SettingsCatalog.MaxVoiceLength) != PatchResult.Applied) return PatchResult.Rejected;
                s.VoiceId = voice!;
                return PatchResult.Applied;

            case "voice.outputDeviceId":
                if (OptionalId(value, out var output, out error) != PatchResult.Applied) return PatchResult.Rejected;
                s.OutputDeviceId = output;
                return PatchResult.Applied;

            case "microphone.deviceId":
                if (OptionalId(value, out var mic, out error) != PatchResult.Applied) return PatchResult.Rejected;
                s.MicrophoneDeviceId = mic;
                return PatchResult.Applied;

            case "dictation.language":
                if (value.ValueKind != JsonValueKind.String || ShellIntentParser.ReadString(value) is not { } code
                    || !SettingsCatalog.Languages.Any(l => l.Code == code))
                    return Reject("That language is not available.", out error);
                s.DictationLanguage = code;
                return PatchResult.Applied;

            case "dictation.dictionary":
                if (value.ValueKind != JsonValueKind.Array) return Reject("The dictionary must be a list of words.", out error);
                var words = new List<string?>();
                foreach (var item in value.EnumerateArray().Take(1_000))
                {
                    if (item.ValueKind != JsonValueKind.String) return Reject("Dictionary words must be text.", out error);
                    words.Add(ShellIntentParser.ReadString(item));
                }
                s.DictionaryWords = SettingsCatalog.CleanDictionary(words);
                return PatchResult.Applied;

            case "cursor.colour":
                if (value.ValueKind != JsonValueKind.String || ShellIntentParser.ReadString(value) is not { } colour
                    || SettingsCatalog.Accents.FirstOrDefault(a => a.Equals(colour, StringComparison.OrdinalIgnoreCase)) is not { } accent)
                    return Reject("Pick one of the offered cursor colours.", out error);
                s.AccentColor = accent;
                return PatchResult.Applied;

            case "access.mode":
                return ApplyAccessMode(s, value, confirmed, out error);

            case "access.denyList":
                if (value.ValueKind != JsonValueKind.String || ShellIntentParser.ReadString(value) is not { } deny)
                    return Reject("The block list must be text.", out error);
                var cleaned = SettingsCatalog.CleanDenyList(deny);
                s.AgentDenyListExtra = cleaned.Length == 0 ? null : cleaned;
                return PatchResult.Applied;

            case "advanced.proxyUrl":
                if (value.ValueKind != JsonValueKind.String || SettingsCatalog.LoopbackUrl(ShellIntentParser.ReadString(value)) is not { } url)
                    return Reject("The proxy must be an http or https address on this computer (localhost, 127.0.0.1 or [::1]), without a username or password.", out error);
                s.LocalProxyBaseUrl = url;
                return PatchResult.Applied;

            case "account.provider":
                var provider = value.ValueKind == JsonValueKind.String ? ShellIntentParser.ReadString(value) : null;
                if (provider is not ("chatgpt" or "openrouter")) return Reject("Provider must be chatgpt or openrouter.", out error);
                s.AuthProvider = provider == "chatgpt" ? AuthProvider.ChatGPT : AuthProvider.OpenRouter;
                return PatchResult.Applied;

            case "account.model":
                if (Model(value, 80, allowEmpty: false, out var model, out error) != PatchResult.Applied) return PatchResult.Rejected;
                s.ChatGptModel = model!;
                return PatchResult.Applied;

            case "account.quickModel":
                if (Model(value, 80, allowEmpty: true, out var quick, out error) != PatchResult.Applied) return PatchResult.Rejected;
                s.ChatGptQuickModel = quick!;
                return PatchResult.Applied;

            case "account.openRouterModel":
                if (Model(value, 120, allowEmpty: false, out var routed, out error) != PatchResult.Applied) return PatchResult.Rejected;
                s.OpenRouterModel = routed!;
                return PatchResult.Applied;

            default:
                return Reject("Unknown setting.", out error);
        }
    }

    private static PatchResult ApplyAccessMode(AppSettings s, JsonElement value, bool confirmed, out string? error)
    {
        error = null;
        var text = value.ValueKind == JsonValueKind.String ? ShellIntentParser.ReadString(value) : null;
        AgentAccessMode? mode = text switch
        {
            "preview" => AgentAccessMode.Preview,
            "confirmEach" => AgentAccessMode.ConfirmEach,
            "auto" => AgentAccessMode.Auto,
            "full" => AgentAccessMode.FullAccess,
            _ => null,
        };
        if (mode is not { } chosen) return Reject("Access must be preview, confirmEach, auto or full.", out error);
        if (chosen is AgentAccessMode.Auto or AgentAccessMode.FullAccess && chosen != s.AgentAccess && !confirmed)
            return PatchResult.RequiresConfirmation;
        s.AgentAccess = chosen;
        return PatchResult.Applied;
    }

    private static PatchResult ApplyHotkey(AppSettings s, string name, JsonElement value,
        Action<AppSettings, HotkeyBinding> set, out string? error)
    {
        error = null;
        if (value.ValueKind != JsonValueKind.Object
            || !value.TryGetProperty("keys", out var keys) || keys.ValueKind != JsonValueKind.Array
            || !value.TryGetProperty("trigger", out var trigger) || trigger.ValueKind != JsonValueKind.String)
            return Reject("A shortcut needs keys and a trigger.", out error);
        HotkeyTrigger? kind = ShellIntentParser.ReadString(trigger) switch
        {
            "hold" => HotkeyTrigger.Hold,
            "doubleTap" => HotkeyTrigger.DoubleTap,
            _ => null,
        };
        if (kind is not { } k) return Reject("The trigger must be hold or doubleTap.", out error);

        var tokens = new List<string?>();
        foreach (var item in keys.EnumerateArray().Take(6))
        {
            if (item.ValueKind != JsonValueKind.String) return Reject("Keys must be text.", out error);
            tokens.Add(ShellIntentParser.ReadString(item));
        }
        if (!HotkeyBinding.TryCreate(tokens, k, out var binding, out error)) return PatchResult.Rejected;

        foreach (var other in Hotkeys.Values)
        {
            if (other.Name != name && other.Get(s).SameChord(binding!))
                return Reject($"That shortcut is already used by {other.Name}. Pick a different one.", out error);
        }
        set(s, binding!);
        return PatchResult.Applied;
    }

    private static PatchResult Text(JsonElement value, int min, out string? text, out string? error, int max = SettingsCatalog.MaxIdLength)
    {
        text = null;
        error = null;
        if (value.ValueKind != JsonValueKind.String || ShellIntentParser.ReadString(value)?.Trim() is not { } t
            || t.Length < min || t.Length > max || t.Any(char.IsControl))
            return Reject("That value is not valid.", out error);
        text = t;
        return PatchResult.Applied;
    }

    /// <summary>null or empty means "system default".</summary>
    private static PatchResult OptionalId(JsonElement value, out string? id, out string? error)
    {
        id = null;
        error = null;
        if (value.ValueKind == JsonValueKind.Null) return PatchResult.Applied;
        if (Text(value, 0, out var text, out error) != PatchResult.Applied) return PatchResult.Rejected;
        id = text!.Length == 0 ? null : text;
        return PatchResult.Applied;
    }

    private static PatchResult Model(JsonElement value, int max, bool allowEmpty, out string? model, out string? error)
    {
        model = null;
        error = null;
        if (value.ValueKind != JsonValueKind.String || ShellIntentParser.ReadString(value)?.Trim() is not { } t
            || (t.Length == 0 ? !allowEmpty : t.Length > max || !ModelId.IsMatch(t)))
            return Reject("That model id is not valid.", out error);
        model = t;
        return PatchResult.Applied;
    }

    private static PatchResult Reject(string message, out string? error)
    {
        error = message;
        return PatchResult.Rejected;
    }

    /// <summary>
    /// Brings hand-edited or older settings into range: invalid hotkeys fall back to their defaults, speed snaps to an
    /// allowed value, lists are capped. Called after loading settings.json.
    /// </summary>
    public static void Normalize(AppSettings s)
    {
        s.TalkHotkey = HotkeyBinding.OrDefault(s.TalkHotkey, HotkeyBinding.DefaultTalk);
        s.TextHotkey = HotkeyBinding.OrDefault(s.TextHotkey, HotkeyBinding.DefaultText);
        s.DictateHotkey = HotkeyBinding.OrDefault(s.DictateHotkey, HotkeyBinding.DefaultDictate);
        s.HandsFreeHotkey = HotkeyBinding.OrDefault(s.HandsFreeHotkey, HotkeyBinding.DefaultHandsFree);
        s.SpeechSpeed = SettingsCatalog.SnapSpeed(s.SpeechSpeed);
        s.DictionaryWords = SettingsCatalog.CleanDictionary(s.DictionaryWords);
        s.VoiceId = string.IsNullOrWhiteSpace(s.VoiceId) ? SettingsCatalog.WindowsDefaultVoice : s.VoiceId;
        s.DictationLanguage = SettingsCatalog.Languages.Any(l => l.Code == s.DictationLanguage) ? s.DictationLanguage : "en";
        if (s.AgentDenyListExtra is { } deny) s.AgentDenyListExtra = SettingsCatalog.CleanDenyList(deny);
    }
}
