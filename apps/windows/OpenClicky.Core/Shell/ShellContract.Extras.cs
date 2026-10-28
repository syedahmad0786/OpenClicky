namespace OpenClicky.Core.Shell;

// The Extras pages (Notes, Clipboard shelf, Tasks, Memory). Mirror of the same blocks in contract.ts.

public sealed record NoteView(string Id, string Title, string Text, string UpdatedAt);

/// <param name="Editable">False when the saved notes could not be read: the page locks editing so they are never overwritten.</param>
public sealed record SettingsNotes(bool Editable, string? Error, IReadOnlyList<NoteView> Items, int Limit)
{
    public static SettingsNotes Empty { get; } = new(true, null, [], LocalNotesStore.MaxNotes);
}

/// <param name="Text">At most <see cref="Settings.ExtrasViews.ClipPreview"/> characters; <paramref name="Truncated"/> says the saved item is longer (copy still sends all of it).</param>
public sealed record ClipView(string Id, string Text, bool Truncated, string? Source, string AddedAt, bool Pinned);

/// <param name="Available">False when the shelf could not be opened, or the classic shelf window has it.</param>
/// <param name="Collecting">Copies from other apps are being saved right now.</param>
/// <param name="NextId">The item Copy next copies.</param>
public sealed record SettingsClipboard(bool Available, bool Collecting, string? Error, IReadOnlyList<ClipView> Items, string? NextId, int Limit)
{
    public static SettingsClipboard Empty { get; } = new(true, false, null, [], null, ClipboardShelf.MaxItems);
}

public sealed record TaskEventView(int Step, string Status, string Message);
public sealed record TaskRunView(string Id, string Task, string Status, string Access, string StartedAt, IReadOnlyList<TaskEventView> Events);

/// <param name="Active">A desktop task is open (Stop applies).</param>
/// <param name="Paused">A paused task can be resumed.</param>
/// <param name="Runs">Newest first.</param>
public sealed record SettingsTasks(bool Active, bool Paused, IReadOnlyList<TaskRunView> Runs)
{
    public static SettingsTasks Empty { get; } = new(false, false, []);
}

public sealed record MemoryFactView(string Id, string Text, bool Learned);

/// <param name="Saved">A memory file exists (Export and Clear apply), even an unreadable one.</param>
public sealed record SettingsMemory(bool Editable, bool Saved, string? Error, string Name, string Personality, string Instructions, IReadOnlyList<MemoryFactView> Facts, int Limit)
{
    public static SettingsMemory Empty { get; } = new(true, false, null, "", "", "", [], CompanionMemoryStore.MaxFacts);
}

public sealed partial record SettingsView
{
    public SettingsNotes Notes { get; init; } = SettingsNotes.Empty;
    public SettingsClipboard Clipboard { get; init; } = SettingsClipboard.Empty;
    public SettingsTasks Tasks { get; init; } = SettingsTasks.Empty;
    public SettingsMemory Memory { get; init; } = SettingsMemory.Empty;
}
