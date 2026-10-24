using System.Text.Json;
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

public sealed record ShellUser(string DisplayName);
public sealed record ShellHotkeys(IReadOnlyList<string> Talk, IReadOnlyList<string> Text, IReadOnlyList<string> Dictate, IReadOnlyList<string> HandsFree);
public sealed record ShellStatus(ShellPhase Phase, int JobsWorking, int JobsNeedYou);
public sealed record ShellNotch(NotchTab Tab, bool Open);
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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? JobId = null);

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
    DocumentView? Document);

public static class ShellContract
{
    public const int Version = 1;

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
        "link.open" => OneOf(m, "target", LinkTargets),
        _ => false,
    };

    private static bool Str(JsonElement m, string name, int max) =>
        m.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && ReadString(v) is { Length: > 0 } s && s.Length <= max;

    private static bool Bool(JsonElement m, string name) =>
        m.TryGetProperty(name, out var v) && (v.ValueKind is JsonValueKind.True or JsonValueKind.False);

    private static bool OneOf(JsonElement m, string name, string[] allowed) =>
        m.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && Array.IndexOf(allowed, ReadString(v)) >= 0;

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
