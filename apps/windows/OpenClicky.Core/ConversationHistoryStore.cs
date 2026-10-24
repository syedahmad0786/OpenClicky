using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace OpenClicky.Core;

public sealed record ConversationTurn(long Id, string User, string Assistant, DateTimeOffset CreatedAtUtc);

/// <summary>Persists text-only Talk turns locally until the user deletes them.</summary>
public static class ConversationHistoryStore
{
    private const string SnapshotKey = "conversation.last_snapshot";
    private static readonly object SyncRoot = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string DefaultPath { get { var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenClicky"); Directory.CreateDirectory(directory); return Path.Combine(directory, "history.json"); } }

    public static IReadOnlyList<(string user, string assistant)> Load(string? path = null, int maxTurns = 10)
    {
        if (maxTurns < 0) throw new ArgumentOutOfRangeException(nameof(maxTurns));
        path ??= DefaultPath;
        lock (SyncRoot)
        {
            using var connection = OpenAndImport(path);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT user_text,assistant_text FROM conversations ORDER BY id DESC LIMIT $limit";
            command.Parameters.AddWithValue("$limit", maxTurns);
            using var reader = command.ExecuteReader();
            var result = new List<(string, string)>();
            while (reader.Read()) result.Add((reader.GetString(0), reader.GetString(1)));
            result.Reverse();
            return result;
        }
    }

    public static ConversationTurn Append(string user, string assistant, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(assistant);
        path ??= DefaultPath;
        lock (SyncRoot)
        {
            using var connection = OpenAndImport(path);
            using var transaction = connection.BeginTransaction();
            var turn = Insert(connection, transaction, user, assistant);
            transaction.Commit();
            return turn;
        }
    }

    public static ConversationTurn? Find(long id, string? path = null)
    {
        if (id <= 0) return null;
        path ??= DefaultPath;
        lock (SyncRoot)
        {
            using var connection = OpenAndImport(path);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT user_text,assistant_text,created_at_utc FROM conversations WHERE id=$id";
            command.Parameters.AddWithValue("$id", id);
            using var reader = command.ExecuteReader();
            return reader.Read() ? new(id, reader.GetString(0), reader.GetString(1), DateTimeOffset.Parse(reader.GetString(2))) : null;
        }
    }

    public static void Save(IEnumerable<(string user, string assistant)> turns, string? path = null, int maxTurns = 10)
    {
        ArgumentNullException.ThrowIfNull(turns);
        path ??= DefaultPath;
        var incoming = turns.ToList();
        lock (SyncRoot)
        {
            using var connection = OpenAndImport(path);
            var prior = ReadAll(connection);
            var snapshot = JsonSerializer.Serialize(incoming, JsonOptions);
            var previousSnapshot = LocalSqliteStore.ReadState(connection, SnapshotKey);
            var overlap = 0;
            if (previousSnapshot != snapshot)
                for (var size = Math.Min(prior.Count, incoming.Count); size > 0; size--)
                    if (prior.TakeLast(size).SequenceEqual(incoming.Take(size))) { overlap = size; break; }
            else if (incoming.Count > 0) overlap = incoming.Count - 1;
            using var transaction = connection.BeginTransaction();
            foreach (var turn in incoming.Skip(overlap)) Insert(connection, transaction, turn.user ?? string.Empty, turn.assistant ?? string.Empty);
            LocalSqliteStore.WriteState(connection, transaction, SnapshotKey, snapshot);
            transaction.Commit();
        }
    }

    public static void Clear(string? path = null, string? memoryPath = null)
    {
        path ??= DefaultPath;
        memoryPath ??= Path.GetFullPath(path).Equals(Path.GetFullPath(DefaultPath), StringComparison.OrdinalIgnoreCase)
            ? CompanionMemoryStore.DefaultPath : path;
        string[] sourceIds;
        lock (SyncRoot)
        {
            using var connection = OpenAndImport(path);
            using (var sources = connection.CreateCommand())
            {
                sources.CommandText = "SELECT CAST(id AS TEXT) FROM conversations";
                using var reader = sources.ExecuteReader();
                var ids = new List<string>();
                while (reader.Read()) ids.Add(reader.GetString(0));
                sourceIds = ids.ToArray();
            }
            CompanionMemoryStore.DeleteSourceLinkedFacts("conversation", sourceIds, memoryPath);
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM conversations; DELETE FROM search_index WHERE kind='conversation'; DELETE FROM state WHERE key=$snapshot";
            command.Parameters.AddWithValue("$snapshot", SnapshotKey);
            command.ExecuteNonQuery();
            transaction.Commit();
        }
    }

    private static SqliteConnection OpenAndImport(string path)
    {
        var connection = LocalSqliteStore.Open(LocalSqliteStore.DatabasePath(path, DefaultPath));
        var source = "conversation:" + Path.GetFullPath(path);
        if (LocalSqliteStore.IsImported(connection, source)) return connection;
        HistoryFile? legacy = null;
        if (File.Exists(path)) legacy = JsonSerializer.Deserialize<HistoryFile>(File.ReadAllText(path), JsonOptions) ?? throw new InvalidDataException("The conversation history file is empty.");
        using var transaction = connection.BeginTransaction();
        foreach (var turn in legacy?.Turns ?? [])
            if (!string.IsNullOrWhiteSpace(turn.User) || !string.IsNullOrWhiteSpace(turn.Assistant)) Insert(connection, transaction, turn.User ?? string.Empty, turn.Assistant ?? string.Empty);
        LocalSqliteStore.MarkImported(connection, transaction, source);
        transaction.Commit();
        return connection;
    }

    private static List<(string user, string assistant)> ReadAll(SqliteConnection connection)
    {
        using var command = connection.CreateCommand(); command.CommandText = "SELECT user_text,assistant_text FROM conversations ORDER BY id";
        using var reader = command.ExecuteReader(); var result = new List<(string, string)>();
        while (reader.Read()) result.Add((reader.GetString(0), reader.GetString(1))); return result;
    }

    private static ConversationTurn Insert(SqliteConnection connection, SqliteTransaction transaction, string user, string assistant)
    {
        var created = DateTimeOffset.UtcNow;
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT INTO conversations(user_text,assistant_text,created_at_utc) VALUES($user,$assistant,$at); SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("$user", user); command.Parameters.AddWithValue("$assistant", assistant); command.Parameters.AddWithValue("$at", created.ToString("O")); var id = (long)command.ExecuteScalar()!;
        LocalSearchStore.Index(connection, transaction, new("conversation", id.ToString(), user, assistant));
        return new(id, user, assistant, created);
    }

    private sealed class HistoryFile { public List<HistoryTurn> Turns { get; set; } = []; }
    private sealed class HistoryTurn { public string? User { get; set; } public string? Assistant { get; set; } }
}
