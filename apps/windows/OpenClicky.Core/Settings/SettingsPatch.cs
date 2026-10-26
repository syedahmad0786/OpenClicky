using System.Text.Json;
using System.Text.RegularExpressions;
using OpenClicky.Core.Shell;

namespace OpenClicky.Core.Settings;

public enum PatchResult { Applied, Rejected, RequiresConfirmation }

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
        ["dictation.cleanup"] = (s, v) => s.DictationCleanup = v,
        ["microphone.accurate"] = (s, v) => s.AccurateSpeech = v,
        ["cursor.hidden"] = (s, v) => s.CursorHidden = v,
        ["cursor.screenHighlight"] = (s, v) => s.ScreenHighlight = v,
        ["cursor.shakeToDetach"] = (s, v) => s.ShakeToDetach = v,
        ["agents.speakAnnouncements"] = (s, v) => s.SpeakJobAnnouncements = v,
        ["agents.cursorUpdates"] = (s, v) => s.CursorJobUpdates = v,
        ["access.rememberHistory"] = (s, v) => s.PersistConversationHistory = v,
        ["advanced.useLocalProxy"] = (s, v) => s.UseLocalProxy = v,
        ["advanced.forceGdiCapture"] = (s, v) => s.ForceGdiCapture = v,
        ["proactive.morningBrief"] = (s, v) => Proactive(s).MorningBrief = v,
        ["proactive.suggestions"] = (s, v) => Proactive(s).Suggestions = v,
        ["proactive.integrationNudges"] = (s, v) => Proactive(s).IntegrationNudges = v,
        ["proactive.meetingCountdown"] = (s, v) => Proactive(s).MeetingCountdown = v,
    };

    private static readonly Regex ConnectorKey = new(@"^[a-z0-9_]{1,80}\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    /// <summary>The proactive block, created when hand-edited JSON left it null so a patch is never lost.</summary>
    private static OpenClicky.Core.Proactive.ProactiveSettings Proactive(AppSettings s) => s.Proactive ??= new();

    private sealed record HotkeySlot(string Name, Func<AppSettings, HotkeyBinding?> Get, Action<AppSettings, HotkeyBinding> Set, HotkeyBinding Default);

    private static readonly Dictionary<string, HotkeySlot> Hotkeys = new()
    {
        ["shortcuts.talk"] = new("Talk", s => s.TalkHotkey, (s, b) => s.TalkHotkey = b, HotkeyBinding.DefaultTalk),
        ["shortcuts.text"] = new("Text mode", s => s.TextHotkey, (s, b) => s.TextHotkey = b, HotkeyBinding.DefaultText),
        ["shortcuts.dictate"] = new("Dictate", s => s.DictateHotkey, (s, b) => s.DictateHotkey = b, HotkeyBinding.DefaultDictate),
        ["shortcuts.handsFree"] = new("Hands-free dictation", s => s.HandsFreeHotkey, (s, b) => s.HandsFreeHotkey = b, HotkeyBinding.DefaultHandsFree),
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

        if (Hotkeys.TryGetValue(key, out var hotkey)) return ApplyHotkey(s, hotkey, value, out error);

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
                    if (ShellIntentParser.ReadString(item) is not { } word) return Reject("Dictionary words must be valid text.", out error);
                    words.Add(word);
                }
                s.DictionaryWords = SettingsCatalog.CleanDictionary(words);
                return PatchResult.Applied;

            case "cursor.colour":
                if (value.ValueKind != JsonValueKind.String || ShellIntentParser.ReadString(value) is not { } colour
                    || !SettingsCatalog.Accents.Contains(colour, StringComparer.OrdinalIgnoreCase))
                    return Reject("Pick one of the offered cursor colours.", out error);
                s.AccentColor = SettingsCatalog.SnapAccent(colour);
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

            case "proactive.calendarConnector":
                var connector = value.ValueKind == JsonValueKind.String ? ShellIntentParser.ReadString(value) : null;
                if (connector is null || (connector.Length > 0 && !ConnectorKey.IsMatch(connector))) return Reject("Pick one of your connectors.", out error);
                Proactive(s).CalendarConnector = connector.Length == 0 ? null : connector;
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

    private static PatchResult ApplyHotkey(AppSettings s, HotkeySlot slot, JsonElement value, out string? error)
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
            // Compared the way the page shows them: a missing or invalid stored binding counts as that action's default.
            if (!ReferenceEquals(other, slot) && HotkeyBinding.OrDefault(other.Get(s), other.Default).SameChord(binding!))
                return Reject($"That shortcut is already used by {other.Name}. Pick a different one.", out error);
        }
        slot.Set(s, binding!);
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
        s.AccentColor = SettingsCatalog.SnapAccent(s.AccentColor);
        s.DictionaryWords = SettingsCatalog.CleanDictionary(s.DictionaryWords);
        s.VoiceId = string.IsNullOrWhiteSpace(s.VoiceId) ? SettingsCatalog.WindowsDefaultVoice : s.VoiceId;
        s.DictationLanguage = SettingsCatalog.Languages.Any(l => l.Code == s.DictationLanguage) ? s.DictationLanguage : "en";
        if (s.AgentDenyListExtra is { } deny) s.AgentDenyListExtra = SettingsCatalog.CleanDenyList(deny);
        Proactive(s);
    }
}
