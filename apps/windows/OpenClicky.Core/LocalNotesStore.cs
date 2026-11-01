using System.Text;
using System.Text.Json;

namespace OpenClicky.Core;

public sealed record LocalNote(string Id, string Title, string Text, DateTimeOffset UpdatedAtUtc);

public enum LocalNotesLoadStatus
{
    Missing,
    Loaded,
    Unreadable,
}

public sealed record LocalNotesLoadResult(
    LocalNotesLoadStatus Status,
    IReadOnlyList<LocalNote>? Notes,
    string? Error
)
{
    public bool CanEdit => Status != LocalNotesLoadStatus.Unreadable;
}

public static class LocalNotesStore
{
    public const int MaxNotes = 20,
        MaxTitleLength = 100,
        MaxTextLength = 10_000,
        MaxFileBytes = 2_097_152;
    private const string StateKey = "notes";
    private static readonly object SyncRoot = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = 8,
    };
    public static string DefaultPath
    {
        get
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OpenClicky"
            );
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, "notes.json");
        }
    }

    public static LocalNotesLoadResult Load(string? path = null)
    {
        path ??= DefaultPath;
        lock (SyncRoot)
        {
            try
            {
                var (notes, exists) = Read(path);
                return new(
                    exists ? LocalNotesLoadStatus.Loaded : LocalNotesLoadStatus.Missing,
                    notes,
                    null
                );
            }
            catch (Exception ex)
                when (ex
                        is IOException
                            or UnauthorizedAccessException
                            or JsonException
                            or InvalidDataException
                            or ArgumentException
                )
            {
                return new(
                    LocalNotesLoadStatus.Unreadable,
                    null,
                    $"The saved notes could not be read and were left unchanged: {ex.Message}"
                );
            }
        }
    }

    public static IReadOnlyList<LocalNote> GetRecords(string? path = null)
    {
        var result = Load(path);
        return result.CanEdit && result.Notes is not null
            ? result.Notes
            : throw new InvalidDataException(result.Error ?? "The saved notes are unreadable.");
    }

    public static LocalNote Add(string title, string text, string? path = null) =>
        SaveNote(null, title, text, path);

    public static LocalNote SaveNote(string? id, string title, string text, string? path = null)
    {
        ValidateText(title, MaxTitleLength, nameof(title), false);
        ValidateText(text, MaxTextLength, nameof(text), true);
        if (id is not null)
            ValidateId(id);
        path ??= DefaultPath;
        lock (SyncRoot)
        {
            var notes = ReadForMutation(path).ToList();
            var now = DateTimeOffset.UtcNow;
            LocalNote saved;
            if (id is null)
            {
                if (notes.Count >= MaxNotes)
                    throw new InvalidOperationException(
                        $"Local notes are limited to {MaxNotes} notes."
                    );
                saved = new(Guid.NewGuid().ToString("N"), title, text, now);
                notes.Add(saved);
            }
            else
            {
                var index = notes.FindIndex(n => n.Id == id);
                if (index < 0)
                    throw new KeyNotFoundException("The note no longer exists.");
                saved = notes[index] with { Title = title, Text = text, UpdatedAtUtc = now };
                notes[index] = saved;
            }
            Write(path, notes);
            return saved;
        }
    }

    public static bool Update(string id, string title, string text, string? path = null)
    {
        try
        {
            SaveNote(id, title, text, path);
            return true;
        }
        catch (KeyNotFoundException)
        {
            return false;
        }
    }

    public static bool Delete(string id, string? path = null)
    {
        ValidateId(id);
        path ??= DefaultPath;
        lock (SyncRoot)
        {
            var notes = ReadForMutation(path).ToList();
            var removed = notes.RemoveAll(n => n.Id == id) != 0;
            if (removed)
                Write(path, notes);
            return removed;
        }
    }

    public static IReadOnlyList<LocalNote> Search(string query, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        var notes = GetRecords(path);
        return string.IsNullOrWhiteSpace(query)
            ? notes
            : notes
                .Where(n =>
                    n.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || n.Text.Contains(query, StringComparison.OrdinalIgnoreCase)
                )
                .ToArray();
    }

    public static void ExportMarkdown(string destinationPath, string? path = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        path ??= DefaultPath;
        if (
            Path.GetFullPath(path)
                .Equals(Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase)
        )
            throw new ArgumentException(
                "The export destination must differ from the notes file.",
                nameof(destinationPath)
            );
        var markdown = new StringBuilder("# OpenClicky Notes\n\n");
        foreach (var note in GetRecords(path))
            markdown
                .Append("## ")
                .Append(note.Title.Replace('\r', ' ').Replace('\n', ' '))
                .Append("\n\n_Updated ")
                .Append(note.UpdatedAtUtc.ToString("u"))
                .Append("_\n\n")
                .Append(note.Text)
                .Append("\n\n");
        File.WriteAllText(destinationPath, markdown.ToString(), new UTF8Encoding(false));
    }

    private static (IReadOnlyList<LocalNote> notes, bool exists) Read(string path)
    {
        using var connection = LocalSqliteStore.Open(
            LocalSqliteStore.DatabasePath(path, DefaultPath)
        );
        var source = "notes:" + Path.GetFullPath(path);
        var json = LocalSqliteStore.ReadState(connection, StateKey);
        if (json is null && !LocalSqliteStore.IsImported(connection, source))
        {
            if (File.Exists(path))
            {
                if (new FileInfo(path).Length > MaxFileBytes)
                    throw new InvalidDataException(
                        $"The notes file exceeds the {MaxFileBytes}-byte limit."
                    );
                var legacy =
                    JsonSerializer.Deserialize<NotesFile>(File.ReadAllText(path), JsonOptions)
                    ?? throw new InvalidDataException("The notes file is empty.");
                if (legacy.Version != 1)
                    throw new InvalidDataException($"Unsupported notes version {legacy.Version}.");
                var imported = legacy.Notes ?? [];
                Validate(imported);
                json = JsonSerializer.Serialize(imported, JsonOptions);
                using var tx = connection.BeginTransaction();
                LocalSqliteStore.WriteState(connection, tx, StateKey, json);
                Index(connection, tx, imported);
                LocalSqliteStore.MarkImported(connection, tx, source);
                tx.Commit();
            }
            else
            {
                using var tx = connection.BeginTransaction();
                LocalSqliteStore.MarkImported(connection, tx, source);
                tx.Commit();
                return (Array.Empty<LocalNote>(), false);
            }
        }
        if (json is null)
            return (Array.Empty<LocalNote>(), false);
        var notes =
            JsonSerializer.Deserialize<List<LocalNote>>(json, JsonOptions)
            ?? throw new InvalidDataException("The notes data is empty.");
        Validate(notes);
        return (notes, true);
    }

    private static IReadOnlyList<LocalNote> ReadForMutation(string path)
    {
        try
        {
            return Read(path).notes;
        }
        catch (Exception ex)
            when (ex
                    is IOException
                        or UnauthorizedAccessException
                        or JsonException
                        or ArgumentException
            )
        {
            throw new InvalidDataException(
                "The existing notes are unreadable and were preserved.",
                ex
            );
        }
    }

    private static void Write(string path, IReadOnlyList<LocalNote> notes)
    {
        Validate(notes);
        var json = JsonSerializer.Serialize(notes, JsonOptions);
        if (Encoding.UTF8.GetByteCount(json) > MaxFileBytes)
            throw new InvalidOperationException(
                $"The notes data exceeds the {MaxFileBytes}-byte limit."
            );
        using var connection = LocalSqliteStore.Open(
            LocalSqliteStore.DatabasePath(path, DefaultPath)
        );
        using var tx = connection.BeginTransaction();
        LocalSqliteStore.WriteState(connection, tx, StateKey, json);
        Index(connection, tx, notes);
        tx.Commit();
    }

    private static void Index(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        Microsoft.Data.Sqlite.SqliteTransaction transaction,
        IEnumerable<LocalNote> notes
    ) =>
        LocalSearchStore.ReplaceKind(
            connection,
            transaction,
            "note",
            notes.Select(note => new LocalSearchResult("note", note.Id, note.Title, note.Text))
        );

    private static void Validate(IReadOnlyList<LocalNote> notes)
    {
        if (notes.Count > MaxNotes)
            throw new InvalidDataException($"Local notes are limited to {MaxNotes} notes.");
        var ids = new HashSet<string>();
        foreach (var note in notes)
        {
            ValidateId(note.Id);
            ValidateText(note.Title, MaxTitleLength, nameof(note.Title), false);
            ValidateText(note.Text, MaxTextLength, nameof(note.Text), true);
            if (note.UpdatedAtUtc == default || !ids.Add(note.Id))
                throw new InvalidDataException(
                    "Notes contain an invalid timestamp or duplicate id."
                );
        }
    }

    private static void ValidateId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (id.Length > 64)
            throw new ArgumentOutOfRangeException(nameof(id));
    }

    private static void ValidateText(string value, int max, string name, bool empty)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!empty && string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be empty.", name);
        if (value.Length > max)
            throw new ArgumentOutOfRangeException(name, $"Value is limited to {max} characters.");
    }

    private sealed class NotesFile
    {
        public int Version { get; set; }
        public List<LocalNote>? Notes { get; set; }
    }
}
