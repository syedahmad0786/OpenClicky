using Microsoft.Data.Sqlite;

namespace OpenClicky.Core;

internal static class LocalSqliteStore
{
    internal static string DatabasePath(string legacyPath, string defaultLegacyPath)
    {
        var full = Path.GetFullPath(legacyPath);
        return full.Equals(Path.GetFullPath(defaultLegacyPath), StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(Path.GetDirectoryName(full)!, "openclicky.db")
            : Path.ChangeExtension(full, ".db");
    }

    internal static SqliteConnection Open(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        try
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
            command.ExecuteNonQuery();
            EnsureSchema(connection);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    internal static bool IsImported(SqliteConnection connection, string source)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM imports WHERE source=$source";
        command.Parameters.AddWithValue("$source", source);
        return command.ExecuteScalar() is not null;
    }

    internal static void MarkImported(SqliteConnection connection, SqliteTransaction transaction, string source)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO imports(source, imported_at_utc) VALUES($source,$at)";
        command.Parameters.AddWithValue("$source", source);
        command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    internal static string? ReadState(SqliteConnection connection, string key)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM state WHERE key=$key";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    internal static void WriteState(SqliteConnection connection, SqliteTransaction transaction, string key, string value)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO state(key,value) VALUES($key,$value) ON CONFLICT(key) DO UPDATE SET value=excluded.value";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    internal static void DeleteState(SqliteConnection connection, SqliteTransaction transaction, string key)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM state WHERE key=$key";
        command.Parameters.AddWithValue("$key", key);
        command.ExecuteNonQuery();
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS imports(source TEXT PRIMARY KEY, imported_at_utc TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS state(key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS conversations(
                id INTEGER PRIMARY KEY AUTOINCREMENT, user_text TEXT NOT NULL, assistant_text TEXT NOT NULL,
                created_at_utc TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS clipboard_items(
                id INTEGER PRIMARY KEY AUTOINCREMENT, position INTEGER NOT NULL, text TEXT NOT NULL,
                source TEXT NULL, added_at_utc TEXT NOT NULL, is_pinned INTEGER NOT NULL DEFAULT 0,
                collection TEXT NULL);
            CREATE TABLE IF NOT EXISTS memory_tombstones(
                source_kind TEXT NOT NULL, source_id TEXT NOT NULL, deleted_at_utc TEXT NOT NULL,
                PRIMARY KEY(source_kind, source_id));
            CREATE VIRTUAL TABLE IF NOT EXISTS search_index USING fts5(kind UNINDEXED, record_id UNINDEXED, title, content);
            """;
        command.ExecuteNonQuery();
    }
}
