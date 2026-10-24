using System.Text;
using System.Text.Json;

namespace OpenClicky.Core;

public sealed record VisualArtifact(string Id, string Kind, string Title, string? FollowupId, string SessionId, string ConversationId, string SourceJson, DateTimeOffset UpdatedAtUtc);

public static class VisualArtifactStore
{
    public const int MaxSourceBytes = 64_000, MaxStoredBytes = 2_097_152;
    private static readonly object SyncRoot = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, MaxDepth = 16 };
    public static string DefaultPath => ConversationHistoryStore.DefaultPath;

    public static VisualArtifact Parse(string sessionId, string conversationId, JsonElement source)
    {
        Text(sessionId, 160, nameof(sessionId)); Text(conversationId, 160, nameof(conversationId));
        var json = source.GetRawText();
        if (Encoding.UTF8.GetByteCount(json) > MaxSourceBytes || source.ValueKind != JsonValueKind.Object) throw new InvalidDataException("The visual artifact is too large or malformed.");
        var kind = Required(source, "kind", 20); var id = Required(source, "id", 160); var title = Required(source, "title", 160);
        var followupId = Optional(source, "followupId", 160);
        var common = new HashSet<string>(["kind", "id", "title", "followupId"], StringComparer.Ordinal);
        switch (kind)
        {
            case "chart": Strict(source, common, "series"); Array(source, "series", 1, 12, item => { Strict(item, [], "label", "value"); Required(item, "label", 160); Number(item, "value"); }); break;
            case "diagram":
                Strict(source, common, "nodes", "edges"); var ids = new HashSet<string>(StringComparer.Ordinal);
                Array(source, "nodes", 1, 12, item => { Strict(item, [], "id", "label", "detail"); if (!ids.Add(Required(item, "id", 160))) throw new InvalidDataException("Diagram node ids must be unique."); Required(item, "label", 160); Present(item, "detail", 600); });
                Array(source, "edges", 0, 18, item => { Strict(item, [], "from", "to"); var from = Required(item, "from", 160); var to = Required(item, "to", 160); if (!ids.Contains(from) || !ids.Contains(to)) throw new InvalidDataException("Diagram edges must reference known nodes."); }); break;
            case "calculator": Strict(source, common, "fields"); var fieldIds = new HashSet<string>(StringComparer.Ordinal); Array(source, "fields", 1, 6, item => { Strict(item, [], "id", "label", "value", "min", "max", "step"); if (!fieldIds.Add(Required(item, "id", 160))) throw new InvalidDataException("Calculator field ids must be unique."); Required(item, "label", 160); var value = Number(item, "value"); var min = Number(item, "min"); var max = Number(item, "max"); var step = Number(item, "step"); if (min > max || value < min || value > max || step <= 0) throw new InvalidDataException("Calculator bounds are invalid."); }); break;
            case "scene3d": Strict(source, common, "shape", "color", "rotation"); if (Required(source, "shape", 16) is not ("cube" or "pyramid")) throw new InvalidDataException("Unknown 3D shape."); var color = Required(source, "color", 7); if (color.Length != 7 || color[0] != '#' || !color[1..].All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid color."); var rotation = Number(source, "rotation"); if (rotation is < -360 or > 360) throw new InvalidDataException("Invalid rotation."); break;
            default: throw new InvalidDataException("Unknown visual artifact kind.");
        }
        return new(id, kind, title, followupId, sessionId, conversationId, json, DateTimeOffset.UtcNow);
    }

    public static VisualArtifact Save(string sessionId, string conversationId, JsonElement source, string? path = null)
    {
        var artifact = Parse(sessionId, conversationId, source); path ??= DefaultPath;
        lock (SyncRoot)
        {
            using var connection = LocalSqliteStore.Open(LocalSqliteStore.DatabasePath(path, DefaultPath));
            var records = Read(connection, sessionId).ToList(); var index = records.FindIndex(item => item.Id == artifact.Id);
            if (index < 0) records.Add(artifact); else records[index] = artifact;
            var json = JsonSerializer.Serialize(records, JsonOptions); if (Encoding.UTF8.GetByteCount(json) > MaxStoredBytes) throw new InvalidOperationException("Saved visual artifacts exceed the local storage limit.");
            using var tx = connection.BeginTransaction(); LocalSqliteStore.WriteState(connection, tx, Key(sessionId), json);
            using (var delete = connection.CreateCommand()) { delete.Transaction = tx; delete.CommandText = "DELETE FROM search_index WHERE kind='visual-artifact' AND record_id=$id"; delete.Parameters.AddWithValue("$id", $"{sessionId}:{artifact.Id}"); delete.ExecuteNonQuery(); }
            LocalSearchStore.Index(connection, tx, new("visual-artifact", $"{sessionId}:{artifact.Id}", artifact.Title, artifact.SourceJson)); tx.Commit(); return artifact;
        }
    }

    public static IReadOnlyList<VisualArtifact> Load(string sessionId, string? path = null)
    {
        Text(sessionId, 160, nameof(sessionId)); path ??= DefaultPath;
        lock (SyncRoot) { using var connection = LocalSqliteStore.Open(LocalSqliteStore.DatabasePath(path, DefaultPath)); return Read(connection, sessionId).OrderByDescending(item => item.UpdatedAtUtc).ToArray(); }
    }

    private static IReadOnlyList<VisualArtifact> Read(Microsoft.Data.Sqlite.SqliteConnection connection, string sessionId)
    {
        var json = LocalSqliteStore.ReadState(connection, Key(sessionId)); if (json is null) return [];
        var values = JsonSerializer.Deserialize<List<VisualArtifact>>(json, JsonOptions) ?? throw new InvalidDataException("Saved artifacts are unreadable.");
        foreach (var value in values) { using var source = JsonDocument.Parse(value.SourceJson); var parsed = Parse(value.SessionId, value.ConversationId, source.RootElement); if (parsed.Id != value.Id || value.SessionId != sessionId) throw new InvalidDataException("Saved artifacts failed validation."); }
        return values;
    }
    private static string Key(string sessionId) => "visual-artifacts:" + sessionId;
    private static void Strict(JsonElement value, IEnumerable<string> common, params string[] allowed) { var names = common.Concat(allowed).ToHashSet(StringComparer.Ordinal); foreach (var property in value.EnumerateObject()) if (!names.Contains(property.Name)) throw new InvalidDataException($"Unknown artifact field {property.Name}."); }
    private static void Array(JsonElement parent, string name, int min, int max, Action<JsonElement> validate) { if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array || value.GetArrayLength() < min || value.GetArrayLength() > max) throw new InvalidDataException($"Invalid {name} collection."); foreach (var item in value.EnumerateArray()) { if (item.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"Invalid {name} item."); validate(item); } }
    private static string Required(JsonElement parent, string name, int max) => Optional(parent, name, max) is { Length: > 0 } value ? value : throw new InvalidDataException($"Missing {name}.");
    private static string Present(JsonElement parent, string name, int max) => Optional(parent, name, max) ?? throw new InvalidDataException($"Missing {name}.");
    private static string? Optional(JsonElement parent, string name, int max) { if (!parent.TryGetProperty(name, out var value)) return null; if (value.ValueKind != JsonValueKind.String) throw new InvalidDataException($"Invalid {name}."); var text = value.GetString()!.Trim(); if (text.Length > max) throw new InvalidDataException($"{name} is too long."); return text; }
    private static double Number(JsonElement parent, string name) { if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) || double.IsNaN(number) || double.IsInfinity(number)) throw new InvalidDataException($"Invalid {name}."); return number; }
    private static void Text(string value, int max, string name) { ArgumentException.ThrowIfNullOrWhiteSpace(value); if (value.Length > max) throw new ArgumentOutOfRangeException(name); }
}
