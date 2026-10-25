using System.Text;
using System.Text.Json;

namespace OpenClicky.Providers;

/// <summary>
/// An Obsidian vault as an in-process connector, so vault access goes through the same connector_tools/connector_call path
/// and access rules as any other connector. Notes are untrusted data. Only plain .md files under the vault root are
/// reachable: no dot-folders (.obsidian), no ".." and no reparse points (junctions or symlinks) anywhere along the way.
/// </summary>
public sealed class ObsidianVaultConnector(string vaultPath) : McpConnector("obsidian", "Obsidian")
{
    public const int ToolCount = 4;
    private const int MaxListed = 500, MaxSearchFiles = 5_000, MaxSearchResults = 20, MaxSearchFileBytes = 1_048_576;
    private const int MaxReadChars = 200_000, MaxWriteChars = 1_000_000, MaxSegments = 8, MaxPathChars = 260, MaxSnippet = 200;
    private static readonly HashSet<string> ReservedNames = new(
        ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
         "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"], StringComparer.OrdinalIgnoreCase);
    private readonly string _root = Path.GetFullPath(vaultPath);

    /// <summary>True when the folder exists and holds a .obsidian subfolder.</summary>
    public static bool IsVault(string? path)
    {
        try { return !string.IsNullOrWhiteSpace(path) && Directory.Exists(Path.Combine(path, ".obsidian")); }
        catch (ArgumentException) { return false; }
        catch (NotSupportedException) { return false; }
    }

    private static readonly IReadOnlyList<RemoteMcpTool> Tools =
    [
        new("list_notes", "List Markdown notes in the user's Obsidian vault, optionally inside one folder.", true,
            """{"type":"object","properties":{"folder":{"type":"string","description":"Optional folder inside the vault, such as Daily."}}}"""),
        new("search_notes", "Find notes whose name or text contains the query. Returns matching lines.", true,
            """{"type":"object","properties":{"query":{"type":"string","description":"Text to look for, 1-200 characters."}},"required":["query"]}"""),
        new("read_note", "Read one note by its path from list_notes or search_notes.", true,
            """{"type":"object","properties":{"path":{"type":"string","description":"Note path such as Daily/2026-10-25.md."}},"required":["path"]}"""),
        new("write_note", "Create a note or replace one note's whole text. Read it first if you are changing it.", false,
            """{"type":"object","properties":{"path":{"type":"string","description":"Note path ending in .md; folders are created."},"content":{"type":"string","description":"The note's complete new text."}},"required":["path","content"]}"""),
    ];

    public override Task<IReadOnlyList<RemoteMcpTool>> ListToolsAsync(CancellationToken token) => Task.FromResult(Tools);

    public override Task<RemoteMcpCallResult> CallToolAsync(string name, JsonElement arguments, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            return Task.FromResult(name switch
            {
                "list_notes" => ListNotes(Text(arguments, "folder", out var bad) ?? "", bad),
                "search_notes" => Search(Text(arguments, "query", out _)),
                "read_note" => Read(Text(arguments, "path", out _)),
                "write_note" => Write(Text(arguments, "path", out _), Text(arguments, "content", out _)),
                _ => Fail("This vault has no such tool."),
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(Fail($"The vault could not be accessed ({ex.GetType().Name})."));
        }
    }

    public override void Dispose() { }

    private RemoteMcpCallResult ListNotes(string folder, bool badFolder)
    {
        var start = _root;
        if (folder.Length > 0)
        {
            var resolved = badFolder ? null : Resolve(folder, note: false);
            if (resolved is null || !Directory.Exists(resolved)) return Fail("That folder is not in the vault.");
            start = resolved;
        }
        var notes = Walk(start).Select(Relative).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        return Ok(new { notes = notes.Take(MaxListed), truncated = notes.Length > MaxListed });
    }

    private RemoteMcpCallResult Search(string? query)
    {
        if (query is not { Length: > 0 and <= 200 }) return Fail("query must be 1 to 200 characters.");
        var results = new List<object>();
        var scanned = 0;
        foreach (var file in Walk(_root).OrderBy(Relative, StringComparer.OrdinalIgnoreCase))
        {
            if (results.Count >= MaxSearchResults || ++scanned > MaxSearchFiles) break;
            var relative = Relative(file);
            if (Path.GetFileName(file).Contains(query, StringComparison.OrdinalIgnoreCase))
                results.Add(new { path = relative, line = 0, text = Path.GetFileName(file) });
            if (new FileInfo(file).Length > MaxSearchFileBytes) continue;
            var number = 0;
            foreach (var line in File.ReadLines(file))
            {
                number++;
                if (results.Count >= MaxSearchResults) break;
                if (line.Contains(query, StringComparison.OrdinalIgnoreCase))
                    results.Add(new { path = relative, line = number, text = line.Trim()[..Math.Min(line.Trim().Length, MaxSnippet)] });
            }
        }
        return Ok(new { results });
    }

    private RemoteMcpCallResult Read(string? path)
    {
        var full = path is null ? null : Resolve(path, note: true);
        if (full is null) return Fail(PathRule);
        if (!File.Exists(full)) return Fail("That note does not exist.");
        using var reader = new StreamReader(full, Encoding.UTF8);
        var buffer = new char[MaxReadChars + 1];
        var count = reader.ReadBlock(buffer, 0, buffer.Length);
        var content = new string(buffer, 0, Math.Min(count, MaxReadChars));
        if (count > MaxReadChars) content += "\n[cut: the note is longer]";
        return Ok(new { path = Relative(full), content });
    }

    private RemoteMcpCallResult Write(string? path, string? content)
    {
        var full = path is null ? null : Resolve(path, note: true);
        if (full is null) return Fail(PathRule);
        if (content is null) return Fail("write_note requires path and content.");
        if (content.Length > MaxWriteChars) return Fail($"A note is limited to {MaxWriteChars} characters.");
        if (Directory.Exists(full)) return Fail("That path is a folder.");
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        // The temp name starts with a dot, so listing and search never see a half-written file.
        var temporary = Path.Combine(Path.GetDirectoryName(full)!, "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temporary, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporary, full, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return Ok(new { path = Relative(full), saved = true, characters = content.Length });
    }

    private const string PathRule = "Use a relative .md path inside the vault from list_notes, without .. or hidden folders.";

    private string Relative(string full) => Path.GetRelativePath(_root, full).Replace('\\', '/');

    /// <summary>Every .md file under <paramref name="start"/>, skipping dot-entries and reparse points.</summary>
    // ponytail: walks the whole tree before sorting; fine for note vaults, cap the walk if one ever holds millions of files.
    private static IEnumerable<string> Walk(string start)
    {
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System };
        var pending = new Stack<string>([start]);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory, "*", options))
            {
                var name = Path.GetFileName(entry);
                if (name.StartsWith('.')) continue;
                if (Directory.Exists(entry)) pending.Push(entry);
                else if (name.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) yield return entry;
            }
        }
    }

    /// <summary>Maps a vault-relative path to a full path, or null when it could leave the vault or is not a plain note.</summary>
    private string? Resolve(string relative, bool note)
    {
        if (string.IsNullOrEmpty(relative) || relative.Length > MaxPathChars || relative.Contains(':') || Path.IsPathRooted(relative)) return null;
        var parts = relative.Replace('\\', '/').Split('/');
        if (parts.Length > MaxSegments || parts.Any(part => part.Length == 0 || part.StartsWith('.') || part.Trim() != part || part.EndsWith('.')
                || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || ReservedNames.Contains(part.Split('.')[0])))
            return null;
        if (note && !parts[^1].EndsWith(".md", StringComparison.OrdinalIgnoreCase)) return null;
        var full = Path.GetFullPath(Path.Combine(_root, Path.Combine(parts)));
        if (!full.StartsWith(_root.TrimEnd('\\') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;
        var current = _root;
        foreach (var part in parts)
        {
            current = Path.Combine(current, part);
            if ((File.Exists(current) || Directory.Exists(current)) && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint)) return null;
        }
        return full;
    }

    /// <summary>The string argument <paramref name="name"/>, or null when absent. <paramref name="wrongType"/> is true when it is present but not a string.</summary>
    private static string? Text(JsonElement arguments, string name, out bool wrongType)
    {
        wrongType = false;
        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind == JsonValueKind.String) return value.GetString();
        wrongType = true;
        return null;
    }

    private static RemoteMcpCallResult Ok(object value) => new(false, JsonSerializer.Serialize(value));
    private static RemoteMcpCallResult Fail(string message) => new(true, message);
}
