using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

namespace OpenClicky.Core.Shell;

// Mirror of apps/companion-workspace/src/shell/contract.ts. Keep both in step;
// tests/fixtures/shell-state.v1.json is parsed by both test suites.

public enum ShellTheme { Dark, Light }
public enum ShellPhase { Idle, Listening, Thinking, Speaking, Working, Paused }
public enum NotchTab { Home, Chat, Settings, Document }
public enum JobState { Queued, Working, NeedsYou, Done, Failed, Stopped, New }
public enum JobColour { Red, Blue, Yellow, Green }
public enum JobFileKind { Doc, Pdf, Sheet, Image, Audio, Video, Link, Folder, Other }
public enum ChatRole { User, Assistant, Event }
public enum NotchCardKind { Activity, TextResponse, JobUpdate, NeedsYou, MutedSpeaker, MicTrouble, Clipboard, DictionaryLearned, DocumentAccess, AppUpdated, Tip, Toast }
public enum CardActionStyle { Primary, Secondary, Danger }
public enum DocumentKind { Markdown, Text, Csv, Pdf }
public enum UpdateState { Idle, Checking, Available, Downloading, Ready, UpToDate, Error }
public enum AccountProvider { Chatgpt, Openrouter }
public enum DeviceState { Active, Unavailable }
public enum WhisperState { Missing, Downloading, Ready }
public enum HotkeyTrigger { Hold, DoubleTap }
public enum ChromeState { NotInstalled, Waiting, Connected }
public enum ConnectorStatus { Connected, Checking, Error, NeedsSignIn }
public enum ReachState { Allowed, Denied, Unknown }
public enum RoutineStatus { Scheduled, Running, Failed, Paused }
/// <summary>Wire form of the agent access mode: preview, confirmEach, auto, full.</summary>
public enum AccessModeView { Preview, ConfirmEach, Auto, Full }

public sealed record ShellUser(string DisplayName);
public sealed record ShellHotkeys(IReadOnlyList<string> Talk, IReadOnlyList<string> Text, IReadOnlyList<string> Dictate, IReadOnlyList<string> HandsFree);
public sealed record ShellStatus(ShellPhase Phase, int JobsWorking, int JobsNeedYou);
public sealed record ShellNotch(NotchTab Tab, bool Open, string Section);
public sealed record JobStat(string Value, string Label);
public sealed record JobFile(string Name, string Path, JobFileKind Kind);
public sealed record PlanStep(string Text, bool Done);

public sealed record JobCard(
    string Id,
    string Title,
    JobState State,
    JobColour Colour,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Summary,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JobStat? Stat,
    IReadOnlyList<string> Bullets,
    IReadOnlyList<JobFile> Files,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LiveLine,
    IReadOnlyList<PlanStep> Plan,
    string UpdatedAt);

public sealed record ChatRow(
    string Id,
    ChatRole Role,
    string Text,
    string At,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? JobId = null,
    // DrawingId is set on the row of a saved drawing; the page offers replay, print and remove for it.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DrawingId = null);

public sealed record ShellChat(IReadOnlyList<ChatRow> Rows);
public sealed record CardAction(string Id, string Label, CardActionStyle Style);

public sealed record NotchCard(
    string Id,
    NotchCardKind Kind,
    string Title,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Body,
    IReadOnlyList<CardAction> Actions,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? AutoDismissMs = null);

public sealed record ConnectedApp(string Id, string Name);
public sealed record ShellApps(IReadOnlyList<ConnectedApp> Connected);
public sealed record ShellGeneral(bool ShowInTaskbar, bool ShowInRecordings, bool LaunchAtLogin, bool Paused);
public sealed record ShellUpdate(string Current, string? Available, UpdateState State, double Progress);
public sealed record DocumentView(string Path, string Name, DocumentKind Kind, string Content, bool Truncated);

public sealed record ShellState(
    int Version,
    ShellTheme Theme,
    string Accent,
    bool ReducedMotion,
    ShellUser User,
    ShellHotkeys Hotkeys,
    ShellStatus Status,
    ShellNotch Notch,
    JobCard? Featured,
    IReadOnlyList<JobCard> Jobs,
    ShellChat Chat,
    IReadOnlyList<NotchCard> Cards,
    ShellApps Apps,
    ShellGeneral General,
    ShellUpdate Update,
    DocumentView? Document,
    SettingsView Settings);

public sealed record Device(string Id, string Name, DeviceState State);
public sealed record Hotkey(IReadOnlyList<string> Keys, HotkeyTrigger Trigger);
public sealed record VoiceOption(string Id, string Name, string Detail, bool Available);
public sealed record DictationLanguage(string Code, string Name, string Native);
public sealed record ConnectorView(string Key, string Name, string Host, ConnectorStatus Status, int? ToolCount);
public sealed record ReachItem(string Id, string Label, ReachState State);
public sealed record RoutineView(string Id, string Title, string Schedule, string? Next, string? Last, RoutineStatus Status);

public sealed record SettingsAccount(
    AccountProvider Provider, string? SignedInAs, string Model, string QuickModel, IReadOnlyList<string> Models,
    bool OpenRouterKeySet, string OpenRouterModel);

public sealed record SettingsVoice(
    string Voice, IReadOnlyList<VoiceOption> Voices, double Speed, bool Captions, bool ReadReplies,
    string? OutputDeviceId, IReadOnlyList<Device> OutputDevices);

public sealed record SettingsMicrophone(string? DeviceId, IReadOnlyList<Device> Devices, WhisperState Whisper, double WhisperProgress);

public sealed record SettingsDictation(
    bool AutoDetect, string Language, IReadOnlyList<DictationLanguage> Languages, IReadOnlyList<string> Dictionary,
    bool Cleanup, WhisperState Multilingual, double MultilingualProgress);

public sealed record SettingsShortcuts(Hotkey Talk, Hotkey Text, Hotkey Dictate, Hotkey HandsFree);
public sealed record SettingsCursor(string Colour, bool Hidden, bool ScreenHighlight, bool ShakeToDetach);
public sealed record SettingsIntegrations(ChromeState Chrome, IReadOnlyList<ConnectorView> Connectors);

public sealed record SettingsAgents(
    string Folder, bool FolderIsDefault, bool SpeakAnnouncements, bool CursorUpdates, IReadOnlyList<ReachItem> Reach);

public sealed record SettingsAccess(AccessModeView Mode, string DenyList, bool RememberHistory);
public sealed record SettingsAdvanced(bool UseLocalProxy, string ProxyUrl, bool ForceGdiCapture);

public sealed record SettingsView(
    SettingsAccount Account,
    SettingsVoice Voice,
    SettingsMicrophone Microphone,
    SettingsDictation Dictation,
    SettingsShortcuts Shortcuts,
    SettingsCursor Cursor,
    SettingsIntegrations Integrations,
    SettingsAgents Agents,
    IReadOnlyList<RoutineView> Routines,
    SettingsAccess Access,
    SettingsAdvanced Advanced)
{
    /// <summary>First-run values; the real builder (ShellSettingsBuilder) fills in what the machine knows.</summary>
    public static SettingsView Default { get; } = new(
        new SettingsAccount(AccountProvider.Chatgpt, null, "", "", [], false, ""),
        new SettingsVoice("windows-default", [new VoiceOption("windows-default", "Windows default", "Installed voice", true)], 1, true, true, null, []),
        new SettingsMicrophone(null, [], WhisperState.Missing, 0),
        new SettingsDictation(true, "en", [new DictationLanguage("en", "English", "English")], [], true, WhisperState.Missing, 0),
        new SettingsShortcuts(
            new Hotkey(["Ctrl", "Alt"], HotkeyTrigger.Hold),
            new Hotkey(["Ctrl"], HotkeyTrigger.DoubleTap),
            new Hotkey(["Ctrl", "Win"], HotkeyTrigger.Hold),
            new Hotkey(["Ctrl", "Win"], HotkeyTrigger.DoubleTap)),
        new SettingsCursor("#3380FF", false, true, true),
        new SettingsIntegrations(ChromeState.NotInstalled, []),
        new SettingsAgents("", true, true, true, []),
        [],
        new SettingsAccess(AccessModeView.ConfirmEach, "", true),
        new SettingsAdvanced(false, "", false));
}

public static class ShellContract
{
    public const int Version = 1;
    public const string DefaultSection = "general";

    /// <summary>The settings sidebar, in order. Also the valid values of notch.section and settings.section.</summary>
    public static readonly IReadOnlyList<string> Sections =
    [
        "account", "general", "voice", "microphone", "dictation", "shortcuts", "cursor", "integrations",
        "agents", "routines", "access", "memory", "notes", "clipboard", "tasks", "advanced",
    ];

    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Serialize(ShellState state) => JsonSerializer.Serialize(state, Options);

    public static ShellState? Deserialize(string json) => JsonSerializer.Deserialize<ShellState>(json, Options);
}

/// <summary>A validated page-to-host message. Payload is the whole message object, cloned.</summary>
public sealed record ShellIntent(string Kind, JsonElement Payload)
{
    public string? GetString(string name) =>
        Payload.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? ShellIntentParser.ReadString(v) : null;

    public bool? GetBool(string name) =>
        Payload.TryGetProperty(name, out var v) && (v.ValueKind is JsonValueKind.True or JsonValueKind.False) ? v.GetBoolean() : null;
}

public static class ShellIntentParser
{
    public const int MaxMessageLength = 128_000;
    private const int MaxTextLength = 8_000, MaxRects = 64, MaxIdLength = 160, MaxPathLength = 4_096;

    private static readonly string[] Tabs = ["home", "chat", "settings", "document"];
    private static readonly string[] GeneralKeys = ["showInTaskbar", "showInRecordings", "launchAtLogin", "paused"];
    private static readonly string[] LinkTargets = ["whatsNew", "bug", "feature"];
    private static readonly Regex PatchKey = new(@"^[a-z]+(\.[a-zA-Z]+)+\z", RegexOptions.CultureInvariant); // linear pattern, input capped at 60 chars before the match
    private const int MaxPatchKeyLength = 60, MaxPatchValueChars = 4_000, MaxPatchValueDepth = 4, MaxActionArgLength = 512;

    /// <summary>drawing.action allow-list: the three replay modes, print, and remove from chat.</summary>
    public static readonly IReadOnlyList<string> DrawingActions = ["replayWindow", "replayFullScreen", "replayBoard", "print", "remove"];

    /// <summary>settings.action allow-list.</summary>
    public static readonly IReadOnlyList<string> SettingsActions =
    [
        "signIn", "signOut", "refreshModels", "manageUsage", "secret.edit", "testMic", "downloadWhisper", "playTestSound",
        "previewVoice", "clearHistory", "exportMemory", "openNotes", "openClipboard", "openTasks", "chooseAgentFolder",
        "resetAgentFolder", "openPrivacySetting", "routine.run", "routine.pause", "routine.resume", "routine.delete",
        "connector.add", "connector.test", "connector.remove", "chrome.setup", "recordHotkey", "resetHotkeys", "openLogs",
    ];

    public static bool TryParse(string? json, out ShellIntent intent)
    {
        intent = null!;
        if (string.IsNullOrEmpty(json) || json.Length > MaxMessageLength) return false;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 20 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("kind", out var kindElement)
                || kindElement.ValueKind != JsonValueKind.String
                || ReadString(kindElement) is not { } kind
                || !IsValid(kind, root)) return false;
            intent = new ShellIntent(kind, root.Clone());
            return true;
        }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException)
        {
            // Hostile input (e.g. lone surrogates) must reject, never throw.
            return false;
        }
    }

    /// <summary>GetString throws on an escaped lone surrogate; treat that as no string.</summary>
    internal static string? ReadString(JsonElement element)
    {
        try { return element.GetString(); }
        catch (InvalidOperationException) { return null; }
    }

    // Unknown kinds fall through to false: this switch is the allow-list.
    private static bool IsValid(string kind, JsonElement m) => kind switch
    {
        "bootstrap.request" or "ready" or "talk.toggle" or "update.check" or "update.install" or "app.quit" => true,
        "surface.regions" => Rects(m),
        "surface.focus" => Bool(m, "wanted"),
        "notch.tab" => OneOf(m, "tab", Tabs),
        "notch.open" => Bool(m, "open"),
        "text.submit" => Str(m, "text", MaxTextLength),
        "general.set" => OneOf(m, "key", GeneralKeys) && Bool(m, "value"),
        "job.open" or "job.stop" or "job.dismiss" => Str(m, "id", MaxIdLength),
        "file.open" or "file.reveal" => Str(m, "path", MaxPathLength),
        "card.action" => Str(m, "cardId", MaxIdLength) && Str(m, "actionId", MaxIdLength),
        "card.dismiss" => Str(m, "cardId", MaxIdLength),
        "drawing.action" => Str(m, "id", MaxIdLength) && OneOf(m, "action", DrawingActions),
        "link.open" => OneOf(m, "target", LinkTargets),
        "settings.section" => OneOf(m, "section", ShellContract.Sections),
        "settings.patch" => Patch(m),
        "settings.action" => OneOf(m, "action", SettingsActions) && OptionalStr(m, "arg", MaxActionArgLength),
        _ => false,
    };

    private static bool Str(JsonElement m, string name, int max) =>
        m.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && ReadString(v) is { Length: > 0 } s && s.Length <= max;

    private static bool Bool(JsonElement m, string name) =>
        m.TryGetProperty(name, out var v) && (v.ValueKind is JsonValueKind.True or JsonValueKind.False);

    /// <summary>Absent is fine; present must be a string (empty allowed) of at most <paramref name="max"/> chars.</summary>
    private static bool OptionalStr(JsonElement m, string name, int max) =>
        !m.TryGetProperty(name, out var v) || (v.ValueKind == JsonValueKind.String && ReadString(v) is { } s && s.Length <= max);

    private static bool OneOf(JsonElement m, string name, IReadOnlyList<string> allowed) =>
        m.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && ReadString(v) is { } s && allowed.Contains(s);

    /// <summary>Shape only: the key must look like a path and the value must be small JSON. S2's SettingsPatch decides what is allowed.</summary>
    private static bool Patch(JsonElement m) =>
        m.TryGetProperty("key", out var key) && key.ValueKind == JsonValueKind.String
        && ReadString(key) is { } k && k.Length <= MaxPatchKeyLength && PatchKey.IsMatch(k)
        && m.TryGetProperty("value", out var value) && value.GetRawText().Length <= MaxPatchValueChars
        && SmallJson(value, MaxPatchValueDepth);

    /// <summary>JsonProperty.Name throws InvalidOperationException on an escaped lone surrogate; TryParse catches that and rejects.</summary>
    private static bool NameIsValid(JsonProperty property)
    {
        _ = property.Name;
        return true;
    }

    /// <summary>At most <paramref name="levels"/> nested arrays/objects; every string and property name must be valid UTF-16 (a lone surrogate throws, TryParse rejects).</summary>
    private static bool SmallJson(JsonElement e, int levels)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.String: return ReadString(e) is not null;
            case JsonValueKind.Array:
                return levels > 0 && e.EnumerateArray().All(item => SmallJson(item, levels - 1));
            case JsonValueKind.Object:
                return levels > 0 && e.EnumerateObject().All(p => NameIsValid(p) && SmallJson(p.Value, levels - 1));
            default: return true;
        }
    }

    private static bool Rects(JsonElement m)
    {
        if (!m.TryGetProperty("rects", out var rects) || rects.ValueKind != JsonValueKind.Array || rects.GetArrayLength() > MaxRects) return false;
        foreach (var rect in rects.EnumerateArray())
        {
            if (rect.ValueKind != JsonValueKind.Object) return false;
            foreach (var side in new[] { "x", "y", "w", "h", "r" })
                if (!rect.TryGetProperty(side, out var n) || n.ValueKind != JsonValueKind.Number) return false;
        }
        return true;
    }
}
