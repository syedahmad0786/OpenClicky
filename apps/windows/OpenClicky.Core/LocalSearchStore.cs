using Microsoft.Data.Sqlite;

namespace OpenClicky.Core;

public sealed record LocalSearchResult(string Kind, string RecordId, string Title, string Content);

public static class LocalSearchStore
{
    public static IReadOnlyList<LocalSearchResult> Search(string query, string? legacyPath = null, int limit = 50)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (limit is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(limit));
        legacyPath ??= ConversationHistoryStore.DefaultPath;
        using var connection = LocalSqliteStore.Open(LocalSqliteStore.DatabasePath(legacyPath, ConversationHistoryStore.DefaultPath));
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT kind,record_id,title,content FROM search_index WHERE search_index MATCH $query ORDER BY rank LIMIT $limit";
        command.Parameters.AddWithValue("$query", LiteralQuery(query));
        command.Parameters.AddWithValue("$limit", limit);
        using var reader = command.ExecuteReader();
        var results = new List<LocalSearchResult>();
        while (reader.Read()) results.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        return results;
    }

    internal static void ReplaceKind(SqliteConnection connection, SqliteTransaction transaction, string kind, IEnumerable<LocalSearchResult> records)
    {
        using (var delete = connection.CreateCommand()) { delete.Transaction = transaction; delete.CommandText = "DELETE FROM search_index WHERE kind=$kind"; delete.Parameters.AddWithValue("$kind", kind); delete.ExecuteNonQuery(); }
        foreach (var record in records) Index(connection, transaction, record);
    }

    internal static void Index(SqliteConnection connection, SqliteTransaction transaction, LocalSearchResult record)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT INTO search_index(kind,record_id,title,content) VALUES($kind,$id,$title,$content)";
        command.Parameters.AddWithValue("$kind", record.Kind); command.Parameters.AddWithValue("$id", record.RecordId); command.Parameters.AddWithValue("$title", record.Title); command.Parameters.AddWithValue("$content", record.Content); command.ExecuteNonQuery();
    }

    private static string LiteralQuery(string query) => string.Join(" ", query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(token => $"\"{token.Replace("\"", "\"\"")}\""));
}
