using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace OpenClicky.Core;

public sealed record CompanionMemory(
    string Name,
    string Personality,
    string Instructions,
    IReadOnlyList<CompanionMemoryItem> Facts
)
{
    public static CompanionMemory Empty { get; } = new("", "", "", []);
}

public sealed record CompanionMemoryItem(
    string Id,
    string Text,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string? SourceKind = null,
    string? SourceId = null
);

public enum CompanionMemoryLoadStatus
{
    Missing,
    Loaded,
    Unreadable,
}

public sealed record CompanionMemoryLoadResult(
    CompanionMemoryLoadStatus Status,
    CompanionMemory? Memory,
    string? Error
)
{
    public bool CanEdit => Status != CompanionMemoryLoadStatus.Unreadable;
}

public static class CompanionMemoryStore
{
    public const int MaxNameLength = 80,
        MaxPersonalityLength = 4_000,
        MaxInstructionsLength = 8_000,
        MaxFactLength = 1_000,
        MaxFacts = 100,
        MaxFileBytes = 1_048_576;
    private const string StateKey = "companion.memory";
    private static readonly object SyncRoot = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = 16,
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
            return Path.Combine(directory, "companion-memory.json");
        }
    }

    public static CompanionMemoryLoadResult Load(string? path = null)
    {
        path ??= DefaultPath;
        lock (SyncRoot)
        {
            try
            {
                var (memory, exists) = Read(path);
                return new(
                    exists ? CompanionMemoryLoadStatus.Loaded : CompanionMemoryLoadStatus.Missing,
                    memory,
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
                    CompanionMemoryLoadStatus.Unreadable,
                    null,
                    $"The saved companion memory could not be read and was left unchanged: {ex.Message}"
                );
            }
        }
    }

    public static void Save(CompanionMemory memory, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(memory);
        path ??= DefaultPath;
        lock (SyncRoot)
        {
            _ = ReadForMutation(path);
            Write(path, memory);
        }
    }

    public static CompanionMemory SetProfile(
        string name,
        string personality,
        string instructions,
        string? path = null
    )
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(personality);
        ArgumentNullException.ThrowIfNull(instructions);
        path ??= DefaultPath;
        lock (SyncRoot)
        {
            var current = ReadForMutation(path);
            var updated = current with
            {
                Name = name,
                Personality = personality,
                Instructions = instructions,
            };
            Write(path, updated);
            return updated;
        }
    }

    public static CompanionMemoryItem AddFact(string text, string? path = null) =>
        Add(text, null, null, path);

    /// <summary>Adds an automatically suggested memory linked to its local source. A user-deleted source is never re-added.</summary>
    public static CompanionMemoryItem? AddSourceLinkedFact(
        string text,
        string sourceKind,
        string sourceId,
        string? path = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ValidateText(text, MaxFactLength, nameof(text), false);
        path ??= DefaultPath;
        lock (SyncRoot)
        {
            var current = ReadForMutation(path);
            var existing = current.Facts.FirstOrDefault(fact =>
                fact.SourceKind == sourceKind && fact.SourceId == sourceId
            );
            if (existing is not null)
            {
                return existing;
            }

            using var connection = Open(path);
            if (HasTombstone(connection, sourceKind, sourceId))
            {
                return null;
            }

            if (current.Facts.Count >= MaxFacts)
            {
                throw new InvalidOperationException(
                    $"Companion memory is limited to {MaxFacts} facts."
                );
            }

            var now = DateTimeOffset.UtcNow;
            var item = new CompanionMemoryItem(
                Guid.NewGuid().ToString("N"),
                text,
                now,
                now,
                sourceKind,
                sourceId
            );
            Write(path, current with { Facts = current.Facts.Append(item).ToArray() });
            return item;
        }
    }

    public static bool UpdateFact(string id, string text, string? path = null)
    {
        ValidateId(id);
        ValidateText(text, MaxFactLength, nameof(text), false);
        path ??= DefaultPath;
        lock (SyncRoot)
        {
            var current = Read(path).memory;
            var facts = current.Facts.ToArray();
            var index = Array.FindIndex(facts, fact => fact.Id == id);
            if (index < 0)
                return false;
            facts[index] = facts[index] with { Text = text, UpdatedAtUtc = DateTimeOffset.UtcNow };
            Write(path, current with { Facts = facts });
            return true;
        }
    }

    public static bool DeleteFact(string id, string? path = null)
    {
        ValidateId(id);
        path ??= DefaultPath;
        lock (SyncRoot)
        {
            var current = Read(path).memory;
            var item = current.Facts.FirstOrDefault(f => f.Id == id);
            if (item is null)
                return false;
            using var connection = Open(path);
            using var tx = connection.BeginTransaction();
            if (item.SourceKind is not null && item.SourceId is not null)
                AddTombstone(connection, tx, item.SourceKind, item.SourceId);
            Write(
                connection,
                tx,
                current with
                {
                    Facts = current.Facts.Where(f => f.Id != id).ToArray(),
                }
            );
            tx.Commit();
            return true;
        }
    }

    public static bool IsSourceDeleted(string sourceKind, string sourceId, string? path = null)
    {
        path ??= DefaultPath;
        lock (SyncRoot)
        {
            using var connection = Open(path);
            return HasTombstone(connection, sourceKind, sourceId);
        }
    }

    public static int DeleteSourceLinkedFacts(
        string sourceKind,
        IReadOnlyCollection<string> sourceIds,
        string? path = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKind);
        ArgumentNullException.ThrowIfNull(sourceIds);
        if (sourceIds.Count == 0) return 0;
        path ??= DefaultPath;
        var ids = sourceIds.ToHashSet(StringComparer.Ordinal);
        foreach (var id in ids) ValidateId(id);
        lock (SyncRoot)
        {
            var current = ReadForMutation(path);
            var removed = current.Facts.Where(f => f.SourceKind == sourceKind && f.SourceId is not null && ids.Contains(f.SourceId)).ToArray();
            using var connection = Open(path);
            using var tx = connection.BeginTransaction();
            foreach (var id in ids) AddTombstone(connection, tx, sourceKind, id);
            if (removed.Length > 0)
                Write(connection, tx, current with { Facts = current.Facts.Except(removed).ToArray() });
            tx.Commit();
            return removed.Length;
        }
    }

    public static void Clear(string? path = null)
    {
        path ??= DefaultPath;
        lock (SyncRoot)
        {
            CompanionMemory current;
            try
            {
                current = ReadForMutation(path);
            }
            catch (InvalidDataException) when (File.Exists(path))
            {
                // Clear is the explicit recovery action offered for an unreadable legacy file.
                File.Delete(path);
                current = CompanionMemory.Empty;
            }
            using var connection = Open(path);
            using var tx = connection.BeginTransaction();
            foreach (var fact in current.Facts)
            {
                if (fact.SourceKind is not null && fact.SourceId is not null)
                {
                    AddTombstone(connection, tx, fact.SourceKind, fact.SourceId);
                }
            }
            LocalSqliteStore.DeleteState(connection, tx, StateKey);
            LocalSearchStore.ReplaceKind(connection, tx, "memory", []);
            tx.Commit();
        }
    }

    public static bool ExportTo(string destinationPath, string? sourcePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        sourcePath ??= DefaultPath;
        if (
            Path.GetFullPath(sourcePath)
                .Equals(Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase)
        )
            throw new ArgumentException(
                "The export destination must differ from the companion memory file.",
                nameof(destinationPath)
            );
        lock (SyncRoot)
        {
            if (
                File.Exists(sourcePath)
                && Load(sourcePath).Status == CompanionMemoryLoadStatus.Unreadable
            )
            {
                File.Copy(sourcePath, destinationPath, true);
                return true;
            }
            var loaded = Load(sourcePath);
            if (loaded.Status == CompanionMemoryLoadStatus.Missing)
                return false;
            File.WriteAllText(
                destinationPath,
                JsonSerializer.Serialize(ToLegacy(loaded.Memory!), JsonOptions),
                new UTF8Encoding(false)
            );
            return true;
        }
    }

    private static CompanionMemoryItem Add(
        string text,
        string? sourceKind,
        string? sourceId,
        string? path
    )
    {
        ValidateText(text, MaxFactLength, nameof(text), false);
        path ??= DefaultPath;
        lock (SyncRoot)
        {
            var current = Read(path).memory;
            if (current.Facts.Count >= MaxFacts)
                throw new InvalidOperationException(
                    $"Companion memory is limited to {MaxFacts} facts."
                );
            var now = DateTimeOffset.UtcNow;
            var item = new CompanionMemoryItem(
                Guid.NewGuid().ToString("N"),
                text,
                now,
                now,
                sourceKind,
                sourceId
            );
            Write(path, current with { Facts = current.Facts.Append(item).ToArray() });
            return item;
        }
    }

    private static (CompanionMemory memory, bool exists) Read(string path)
    {
        using var connection = Open(path);
        var source = "memory:" + Path.GetFullPath(path);
        var json = LocalSqliteStore.ReadState(connection, StateKey);
        if (json is null && !LocalSqliteStore.IsImported(connection, source))
        {
            if (File.Exists(path))
            {
                if (new FileInfo(path).Length > MaxFileBytes)
                    throw new InvalidDataException(
                        $"Companion memory exceeds the {MaxFileBytes}-byte limit."
                    );
                var legacy =
                    JsonSerializer.Deserialize<MemoryFile>(File.ReadAllText(path), JsonOptions)
                    ?? throw new InvalidDataException("The companion memory file is empty.");
                if (legacy.Version != 1)
                    throw new InvalidDataException(
                        $"Unsupported companion memory version {legacy.Version}."
                    );
                var imported = FromLegacy(legacy);
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
                return (CompanionMemory.Empty, false);
            }
        }
        if (json is null)
            return (CompanionMemory.Empty, false);
        var memory =
            JsonSerializer.Deserialize<CompanionMemory>(json, JsonOptions)
            ?? throw new InvalidDataException("Companion memory is empty.");
        Validate(memory);
        return (memory, true);
    }

    private static CompanionMemory ReadForMutation(string path)
    {
        try
        {
            return Read(path).memory;
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
                "The existing companion memory is unreadable and was preserved.",
                ex
            );
        }
    }

    private static SqliteConnection Open(string path) =>
        LocalSqliteStore.Open(LocalSqliteStore.DatabasePath(path, DefaultPath));

    private static void Write(string path, CompanionMemory memory)
    {
        using var connection = Open(path);
        using var tx = connection.BeginTransaction();
        Write(connection, tx, memory);
        tx.Commit();
    }

    private static void Write(
        SqliteConnection connection,
        SqliteTransaction tx,
        CompanionMemory memory
    )
    {
        Validate(memory);
        var json = JsonSerializer.Serialize(memory, JsonOptions);
        if (Encoding.UTF8.GetByteCount(json) > MaxFileBytes)
            throw new InvalidOperationException(
                $"Companion memory exceeds the {MaxFileBytes}-byte limit."
            );
        LocalSqliteStore.WriteState(connection, tx, StateKey, json);
        Index(connection, tx, memory);
    }

    private static void Index(
        SqliteConnection connection,
        SqliteTransaction tx,
        CompanionMemory memory
    ) =>
        LocalSearchStore.ReplaceKind(
            connection,
            tx,
            "memory",
            memory.Facts.Select(fact => new LocalSearchResult("memory", fact.Id, "", fact.Text))
        );

    private static bool HasTombstone(SqliteConnection connection, string kind, string id)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT 1 FROM memory_tombstones WHERE source_kind=$kind AND source_id=$id";
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteScalar() is not null;
    }

    private static void AddTombstone(
        SqliteConnection connection,
        SqliteTransaction tx,
        string kind,
        string id
    )
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText =
            "INSERT OR IGNORE INTO memory_tombstones(source_kind,source_id,deleted_at_utc) VALUES($kind,$id,$at)";
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void Validate(CompanionMemory memory)
    {
        ValidateText(memory.Name, MaxNameLength, nameof(memory.Name), true);
        ValidateText(memory.Personality, MaxPersonalityLength, nameof(memory.Personality), true);
        ValidateText(memory.Instructions, MaxInstructionsLength, nameof(memory.Instructions), true);
        if (memory.Facts.Count > MaxFacts)
            throw new ArgumentOutOfRangeException(nameof(memory));
        var ids = new HashSet<string>();
        foreach (var fact in memory.Facts)
        {
            ValidateId(fact.Id);
            ValidateText(fact.Text, MaxFactLength, nameof(fact.Text), false);
            if (
                !ids.Add(fact.Id)
                || fact.CreatedAtUtc == default
                || fact.UpdatedAtUtc < fact.CreatedAtUtc
            )
                throw new InvalidDataException("Companion memory contains invalid facts.");
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

    private static CompanionMemory FromLegacy(MemoryFile f) =>
        new(
            f.Name ?? "",
            f.Personality ?? "",
            f.Instructions ?? "",
            f.Facts?.Select(x =>
                    x is null
                        ? throw new InvalidDataException("Companion memory contains an empty fact.")
                        : new CompanionMemoryItem(
                            x.Id ?? "",
                            x.Text ?? "",
                            x.CreatedAtUtc,
                            x.UpdatedAtUtc,
                            x.SourceKind,
                            x.SourceId
                        )
                )
                .ToArray()
                ?? []
        );

    private static MemoryFile ToLegacy(CompanionMemory m) =>
        new()
        {
            Version = 1,
            Name = m.Name,
            Personality = m.Personality,
            Instructions = m.Instructions,
            Facts = m
                .Facts.Select<CompanionMemoryItem, MemoryItemFile?>(f => new MemoryItemFile
                {
                    Id = f.Id,
                    Text = f.Text,
                    CreatedAtUtc = f.CreatedAtUtc,
                    UpdatedAtUtc = f.UpdatedAtUtc,
                    SourceKind = f.SourceKind,
                    SourceId = f.SourceId,
                })
                .ToList(),
        };

    private sealed class MemoryFile
    {
        public int Version { get; set; }
        public string? Name { get; set; }
        public string? Personality { get; set; }
        public string? Instructions { get; set; }
        public List<MemoryItemFile?>? Facts { get; set; }
    }

    private sealed class MemoryItemFile
    {
        public string? Id { get; set; }
        public string? Text { get; set; }
        public DateTimeOffset CreatedAtUtc { get; set; }
        public DateTimeOffset UpdatedAtUtc { get; set; }
        public string? SourceKind { get; set; }
        public string? SourceId { get; set; }
    }
}
