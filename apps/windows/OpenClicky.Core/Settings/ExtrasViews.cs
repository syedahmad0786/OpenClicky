using System.Globalization;
using System.Text.Json;
using OpenClicky.Core.Shell;

namespace OpenClicky.Core.Settings;

/// <summary>The Extras pages' data (notes, clipboard shelf, task history, companion memory) as the shell shows it.</summary>
public static class ExtrasViews
{
    /// <summary>Clipboard text in the state is cut to this; copy and edit go through the host, which has the whole item.</summary>
    public const int ClipPreview = 2_000;
    public const int TaskText = 400, TaskEvents = 20, TaskEventText = 300, StatusText = 40, ErrorText = 300, SourceText = 160;

    public static SettingsNotes Notes(LocalNotesLoadResult result) => result.CanEdit && result.Notes is { } notes
        ? new(true, null, [.. notes.OrderByDescending(n => n.UpdatedAtUtc).Select(n => new NoteView(n.Id, n.Title, n.Text, ShellStateBuilder.Iso(n.UpdatedAtUtc)))], LocalNotesStore.MaxNotes)
        : NotesUnavailable(result.Error);

    public static SettingsNotes NotesUnavailable(string? error) =>
        new(false, error ?? "The saved notes could not be read and were left unchanged.", [], LocalNotesStore.MaxNotes);

    public static SettingsMemory Memory(CompanionMemoryLoadResult result) => result.CanEdit && result.Memory is { } m
        ? new(true, result.Status == CompanionMemoryLoadStatus.Loaded, null, m.Name, m.Personality, m.Instructions,
            [.. m.Facts.Select(f => new MemoryFactView(f.Id, f.Text, f.SourceKind is not null))], CompanionMemoryStore.MaxFacts)
        : MemoryUnavailable(result.Error);

    /// <summary>Unreadable memory: editing locked, Export and Clear still offered so the user can recover.</summary>
    public static SettingsMemory MemoryUnavailable(string? error) =>
        new(false, true, error ?? "The saved memory could not be read and was left unchanged.", "", "", "", [], CompanionMemoryStore.MaxFacts);

    public static SettingsClipboard Clipboard(IReadOnlyList<ClipboardShelfItem> items, long? nextId, bool collecting) => new(
        true, collecting, null,
        [.. items.Select(i => new ClipView(Id(i.Id), SettingsCatalog.Cut(i.Text, ClipPreview), i.Text.Length > ClipPreview, i.Source, ShellStateBuilder.Iso(i.AddedAtUtc), i.IsPinned))],
        nextId is { } next ? Id(next) : null, ClipboardShelf.MaxItems);

    public static SettingsClipboard ClipboardUnavailable(string error) => new(false, false, error, [], null, ClipboardShelf.MaxItems);

    /// <summary>Runs newest first, each with its last <see cref="TaskEvents"/> steps.</summary>
    public static SettingsTasks Tasks(IReadOnlyList<TaskRunRecord> runs, bool active, bool paused) => new(active, paused,
        [.. runs.Reverse().Take(TaskHistoryStore.MaxRuns).Select(r => new TaskRunView(r.Id, r.Task, r.Status, r.AccessMode, ShellStateBuilder.Iso(r.StartedAt),
            [.. (r.Events ?? []).TakeLast(TaskEvents).Select(e => new TaskEventView(e.Step, e.Status, e.Message))]))]);

    private static string Id(long id) => id.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Parses the args of the Extras settings actions. Every check the stores make is made here first, so a bad arg is refused, never half-applied.</summary>
public static class ExtrasArgs
{
    /// <summary>Text the page may add to or edit on the clipboard shelf (the shelf itself allows more).</summary>
    public const int MaxClipText = 10_000;

    /// <summary>note.save: {"id"?, "title", "text"}; no id (or an empty one) adds a note.</summary>
    public static bool TryNote(string? arg, out string? id, out string title, out string text)
    {
        id = null; title = text = "";
        if (Fields(arg, "id", "title", "text") is not { } f) return false;
        id = string.IsNullOrEmpty(f["id"]) ? null : f["id"];
        title = f["title"]?.Trim() ?? "";
        text = f["text"] ?? "";
        return (id is null || IsId(id)) && title.Length is > 0 and <= LocalNotesStore.MaxTitleLength && text.Length <= LocalNotesStore.MaxTextLength;
    }

    /// <summary>memory.profile: {"name", "personality", "instructions"}, each within the memory store's limits (empty allowed).</summary>
    public static bool TryProfile(string? arg, out string name, out string personality, out string instructions)
    {
        name = personality = instructions = "";
        if (Fields(arg, "name", "personality", "instructions") is not { } f) return false;
        name = f["name"]?.Trim() ?? "";
        personality = f["personality"] ?? "";
        instructions = f["instructions"] ?? "";
        return name.Length <= CompanionMemoryStore.MaxNameLength && personality.Length <= CompanionMemoryStore.MaxPersonalityLength
            && instructions.Length <= CompanionMemoryStore.MaxInstructionsLength;
    }

    /// <summary>memory.add's arg: the memory itself, non-blank and within the fact limit.</summary>
    public static string? Fact(string? arg) =>
        arg?.Trim() is { Length: > 0 and <= CompanionMemoryStore.MaxFactLength } fact ? fact : null;

    /// <summary>clipboard.add's arg: any non-empty text up to <see cref="MaxClipText"/>.</summary>
    public static string? ClipText(string? arg) => arg is { Length: > 0 and <= MaxClipText } && !string.IsNullOrWhiteSpace(arg) ? arg : null;

    /// <summary>memory.update and clipboard.edit: {"id", "text"} with a non-blank text of at most <paramref name="maxText"/>.</summary>
    public static bool TryIdText(string? arg, int maxText, out string id, out string text)
    {
        id = text = "";
        if (Fields(arg, "id", "text") is not { } f || f["id"] is not { } i || !IsId(i) || f["text"] is not { } t) return false;
        id = i;
        text = t;
        return t.Length <= maxText && !string.IsNullOrWhiteSpace(t);
    }

    /// <summary>A note, memory or task run id: what the stores issue (at most 64 characters, no spaces or control characters).</summary>
    public static bool IsId(string? id) => id is { Length: > 0 and <= 64 } && id.All(c => c > ' ' && c != '\u007f');

    /// <summary>A clipboard item id: the positive integer the shelf issued.</summary>
    public static long? ClipId(string? arg) =>
        arg is { Length: > 0 and <= 19 } && long.TryParse(arg, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;

    /// <summary>A flat JSON object whose listed fields are strings or absent; anything else (nesting, other kinds, bad UTF-16) is null.</summary>
    private static Dictionary<string, string?>? Fields(string? arg, params string[] names)
    {
        if (string.IsNullOrEmpty(arg) || arg.Length > ShellIntentParser.MaxTextArgLength) return null;
        try
        {
            using var document = JsonDocument.Parse(arg, new JsonDocumentOptions { MaxDepth = 1 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            var fields = new Dictionary<string, string?>();
            foreach (var name in names)
            {
                if (!document.RootElement.TryGetProperty(name, out var value)) { fields[name] = null; continue; }
                if (value.ValueKind != JsonValueKind.String || ShellIntentParser.ReadString(value) is not { } s) return null;
                fields[name] = s;
            }
            return fields;
        }
        catch (JsonException) { return null; }
    }
}
