using System.Text.Json;

namespace OpenClicky.Core;

/// <summary>Persists the last N Talk turns under %AppData% (text only — no screenshots).</summary>
public static class ConversationHistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string DefaultPath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OpenClicky");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "history.json");
        }
    }

    public static IReadOnlyList<(string user, string assistant)> Load(string? path = null, int maxTurns = 10)
    {
        path ??= DefaultPath;
        if (!File.Exists(path))
        {
            return Array.Empty<(string, string)>();
        }

        try
        {
            var json = File.ReadAllText(path);
            var doc = JsonSerializer.Deserialize<HistoryFile>(json, JsonOptions);
            if (doc?.Turns is null || doc.Turns.Count == 0)
            {
                return Array.Empty<(string, string)>();
            }

            return doc.Turns
                .TakeLast(maxTurns)
                .Select(t => (t.User ?? string.Empty, t.Assistant ?? string.Empty))
                .Where(t => !string.IsNullOrWhiteSpace(t.Item1) || !string.IsNullOrWhiteSpace(t.Item2))
                .ToList();
        }
        catch
        {
            return Array.Empty<(string, string)>();
        }
    }

    public static void Save(
        IEnumerable<(string user, string assistant)> turns,
        string? path = null,
        int maxTurns = 10)
    {
        path ??= DefaultPath;
        var list = turns.TakeLast(maxTurns)
            .Select(t => new HistoryTurn { User = t.user, Assistant = t.assistant })
            .ToList();
        var json = JsonSerializer.Serialize(new HistoryFile { Turns = list }, JsonOptions);
        File.WriteAllText(path, json);
    }

    public static void Clear(string? path = null)
    {
        path ??= DefaultPath;
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed class HistoryFile
    {
        public List<HistoryTurn> Turns { get; set; } = new();
    }

    private sealed class HistoryTurn
    {
        public string? User { get; set; }
        public string? Assistant { get; set; }
    }
}
