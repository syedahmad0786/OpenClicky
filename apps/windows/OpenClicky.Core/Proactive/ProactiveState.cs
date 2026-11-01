using System.Text.Json;

namespace OpenClicky.Core.Proactive;

/// <summary>
/// What the proactive layer remembers across restarts: the day the brief was shown, the nudge and suggestion state,
/// and the meetings the user dismissed (event id → its start, so old ones can be forgotten). Ids and times only.
/// </summary>
public sealed class ProactiveState
{
    public string? BriefDay { get; set; }
    public NudgeState Nudges { get; set; } = new();
    public SuggestionState Suggestions { get; set; } = new();
    public Dictionary<string, DateTimeOffset> DismissedMeetings { get; set; } = [];
}

/// <summary>Reads and writes <see cref="ProactiveState"/> as one JSON file.</summary>
public static class ProactiveStore
{
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenClicky", "proactive.json");

    /// <summary>A missing, unreadable or corrupt file gives a new state; never throws for IO or JSON errors.</summary>
    public static ProactiveState Load(string? path = null)
    {
        try
        {
            var s = JsonSerializer.Deserialize<ProactiveState>(File.ReadAllText(path ?? DefaultPath));
            return s is null ? new() : Normalise(s);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return new(); }
    }

    /// <summary>Writes a temp file next to the target, then moves it over, so a crash never leaves half a file.</summary>
    public static void Save(ProactiveState state, string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(state));
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    // JSON can say null for any collection or element; the rest of the layer assumes none are null.
    private static ProactiveState Normalise(ProactiveState s)
    {
        s.Nudges ??= new();
        s.Nudges.Shown ??= [];
        s.Nudges.SnoozedUntil ??= [];
        s.Nudges.Never ??= [];
        s.Nudges.Never.RemoveAll(n => n is null);
        s.Suggestions ??= new();
        s.Suggestions.Pending ??= [];
        s.Suggestions.Pending.RemoveAll(p => p is null || p.Id is null || p.Title is null || p.Description is null || p.Prompt is null);
        s.Suggestions.Declined ??= [];
        s.Suggestions.Declined.RemoveAll(d => d is null);
        s.DismissedMeetings ??= [];
        return s;
    }
}
