using Microsoft.Data.Sqlite;

namespace OpenClicky.Core;

public enum ClipboardShelfAddResult { Added, DuplicateIgnored, Full, TooLarge }

public sealed record ClipboardShelfItem(long Id, string Text, string? Source, DateTimeOffset AddedAtUtc, bool IsPinned = false, string? Collection = null);

public sealed class ClipboardShelf
{
    public const int MaxItems = 100;
    public const int MaxItemLength = 100_000;
    public const int MaxTotalLength = 500_000;
    private readonly List<ClipboardShelfItem> _items = [];
    private readonly string _databasePath;
    private int _nextIndex;

    public ClipboardShelf(string? path = null)
    {
        var legacy = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenClicky", "clipboard-shelf.json");
        _databasePath = LocalSqliteStore.DatabasePath(legacy, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenClicky", "clipboard-shelf.json"));
        Load();
    }

    public IReadOnlyList<ClipboardShelfItem> Items => _items;
    public bool IsCollecting { get; private set; }

    public ClipboardShelfAddResult TryAdd(string text, string? source, DateTimeOffset? addedAtUtc = null, string? collection = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > MaxItemLength || _items.Sum(item => item.Text.Length) + text.Length > MaxTotalLength) return ClipboardShelfAddResult.TooLarge;
        if (_items.Count == MaxItems) return ClipboardShelfAddResult.Full;
        if (_items.Count > 0 && string.Equals(_items[^1].Text, text, StringComparison.Ordinal)) return ClipboardShelfAddResult.DuplicateIgnored;
        var at = addedAtUtc ?? DateTimeOffset.UtcNow;
        using var connection = LocalSqliteStore.Open(_databasePath); using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT INTO clipboard_items(position,text,source,added_at_utc,is_pinned,collection) VALUES($position,$text,$source,$at,0,$collection); SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("$position", _items.Count);
        command.Parameters.AddWithValue("$text", text);
        command.Parameters.AddWithValue("$source", (object?)(string.IsNullOrWhiteSpace(source) ? null : source) ?? DBNull.Value);
        command.Parameters.AddWithValue("$at", at.ToString("O"));
        command.Parameters.AddWithValue("$collection", (object?)collection ?? DBNull.Value);
        var id = (long)command.ExecuteScalar()!; var item = new ClipboardShelfItem(id, text, string.IsNullOrWhiteSpace(source) ? null : source, at, false, collection);
        LocalSearchStore.Index(connection, transaction, SearchResult(item)); transaction.Commit(); _items.Add(item);
        return ClipboardShelfAddResult.Added;
    }

    public bool Edit(long id, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var index = _items.FindIndex(item => item.Id == id);
        if (index < 0) return false;
        if (text.Length > MaxItemLength || _items.Sum(item => item.Text.Length) - _items[index].Text.Length + text.Length > MaxTotalLength) throw new ArgumentOutOfRangeException(nameof(text));
        var updated = _items[index] with { Text = text }; Persist(updated); _items[index] = updated; return true;
    }

    public bool SetPinned(long id, bool pinned) => Update(id, item => item with { IsPinned = pinned });
    public bool SetCollection(long id, string? collection) => Update(id, item => item with { Collection = collection });

    public void SetCollecting(bool collecting)
    {
        using var connection = LocalSqliteStore.Open(_databasePath); using var transaction = connection.BeginTransaction();
        LocalSqliteStore.WriteState(connection, transaction, "clipboard.collecting", collecting ? "1" : "0"); transaction.Commit(); IsCollecting = collecting;
    }

    public bool Remove(long id)
    {
        var index = _items.FindIndex(item => item.Id == id); if (index < 0) return false;
        var updated = _items.Where(item => item.Id != id).ToList(); var nextIndex = _nextIndex;
        if (index < nextIndex) nextIndex--; if (nextIndex >= updated.Count) nextIndex = 0;
        using var connection = LocalSqliteStore.Open(_databasePath); using var transaction = connection.BeginTransaction();
        using (var command = connection.CreateCommand()) { command.Transaction = transaction; command.CommandText = "DELETE FROM clipboard_items WHERE id=$id"; command.Parameters.AddWithValue("$id", id); command.ExecuteNonQuery(); }
        Reposition(connection, transaction, updated); Reindex(connection, transaction, updated); LocalSqliteStore.WriteState(connection, transaction, "clipboard.next_index", nextIndex.ToString()); transaction.Commit(); _items.Clear(); _items.AddRange(updated); _nextIndex = nextIndex; return true;
    }

    public string Combine(string separator) => string.Join(separator ?? throw new ArgumentNullException(nameof(separator)), _items.Select(item => item.Text));
    public ClipboardShelfItem? PeekNext() => _items.Count == 0 ? null : _items[_nextIndex];

    public bool AdvanceNext(long expectedItemId)
    {
        if (_items.Count == 0 || _items[_nextIndex].Id != expectedItemId) return false;
        var next = (_nextIndex + 1) % _items.Count;
        PersistNextIndex(next);
        _nextIndex = next;
        return true;
    }

    public ClipboardShelfItem? TakeNext()
    {
        var item = PeekNext();
        if (item is not null) AdvanceNext(item.Id);
        return item;
    }

    public bool Move(long id, int newIndex)
    {
        if (newIndex < 0 || newIndex >= _items.Count) throw new ArgumentOutOfRangeException(nameof(newIndex));
        var oldIndex = _items.FindIndex(item => item.Id == id); if (oldIndex < 0) return false;
        var updated = _items.ToList(); var item = updated[oldIndex]; updated.RemoveAt(oldIndex); updated.Insert(newIndex, item);
        using var connection = LocalSqliteStore.Open(_databasePath); using var transaction = connection.BeginTransaction(); Reposition(connection, transaction, updated); LocalSqliteStore.WriteState(connection, transaction, "clipboard.next_index", "0"); transaction.Commit(); _items.Clear(); _items.AddRange(updated); _nextIndex = 0; return true;
    }

    private bool Update(long id, Func<ClipboardShelfItem, ClipboardShelfItem> update)
    {
        var index = _items.FindIndex(item => item.Id == id); if (index < 0) return false;
        var updated = update(_items[index]); Persist(updated); _items[index] = updated; return true;
    }

    private void Load()
    {
        using var connection = LocalSqliteStore.Open(_databasePath);
        IsCollecting = LocalSqliteStore.ReadState(connection, "clipboard.collecting") == "1";
        _nextIndex = int.TryParse(LocalSqliteStore.ReadState(connection, "clipboard.next_index"), out var savedIndex) ? savedIndex : 0;
        using var command = connection.CreateCommand(); command.CommandText = "SELECT id,text,source,added_at_utc,is_pinned,collection FROM clipboard_items ORDER BY position,id";
        using var reader = command.ExecuteReader();
        while (reader.Read()) _items.Add(new(reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), DateTimeOffset.Parse(reader.GetString(3)), reader.GetInt64(4) != 0, reader.IsDBNull(5) ? null : reader.GetString(5)));
        if (_nextIndex >= _items.Count) _nextIndex = 0;
    }

    private void Persist(ClipboardShelfItem item)
    {
        using var connection = LocalSqliteStore.Open(_databasePath); using var transaction = connection.BeginTransaction(); using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "UPDATE clipboard_items SET text=$text,is_pinned=$pinned,collection=$collection WHERE id=$id";
        command.Parameters.AddWithValue("$text", item.Text); command.Parameters.AddWithValue("$pinned", item.IsPinned); command.Parameters.AddWithValue("$collection", (object?)item.Collection ?? DBNull.Value); command.Parameters.AddWithValue("$id", item.Id); command.ExecuteNonQuery(); Reindex(connection, transaction, _items.Select(existing => existing.Id == item.Id ? item : existing)); transaction.Commit();
    }

    private static void Reposition(SqliteConnection connection, SqliteTransaction transaction, IReadOnlyList<ClipboardShelfItem> items)
    {
        for (var index = 0; index < items.Count; index++) { using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "UPDATE clipboard_items SET position=$position WHERE id=$id"; command.Parameters.AddWithValue("$position", index); command.Parameters.AddWithValue("$id", items[index].Id); command.ExecuteNonQuery(); }
    }
    private void PersistNextIndex(int index) { using var connection = LocalSqliteStore.Open(_databasePath); using var transaction = connection.BeginTransaction(); LocalSqliteStore.WriteState(connection, transaction, "clipboard.next_index", index.ToString()); transaction.Commit(); }
    private static LocalSearchResult SearchResult(ClipboardShelfItem item) => new("clipboard", item.Id.ToString(), item.Source ?? "", item.Text);
    private static void Reindex(SqliteConnection connection, SqliteTransaction transaction, IEnumerable<ClipboardShelfItem> items) => LocalSearchStore.ReplaceKind(connection, transaction, "clipboard", items.Select(SearchResult));
}
