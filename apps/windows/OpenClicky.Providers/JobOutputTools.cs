using System.Text;
using System.Text.Json;
using OpenClicky.Core;

namespace OpenClicky.Providers;

/// <summary>Lets one job run save text deliverables inside its own output folder and nowhere else.</summary>
public sealed class JobOutputTools
{
    public const int MaxFileCharacters = 1_000_000;
    public const int MaxFiles = 200;
    public static readonly string[] ToolNames = ["write_output", "list_outputs"];
    private static readonly HashSet<string> BlockedExtensions = new(
        [".exe", ".dll", ".sys", ".bat", ".cmd", ".com", ".ps1", ".psm1", ".psd1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh",
         ".msi", ".msp", ".scr", ".pif", ".cpl", ".lnk", ".url", ".reg", ".hta", ".jar", ".appx", ".msix"],
        StringComparer.OrdinalIgnoreCase);
    private readonly string _root;

    public JobOutputTools(string outputDirectory)
    {
        _root = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(_root);
    }

    public string OutputDirectory => _root;

    internal static IEnumerable<object> Definitions(Func<string, string, object, string[], object> tool) =>
    [
        tool("write_output", "Save one finished text deliverable (Markdown, CSV, HTML, JSON, code…) in this job's output folder. path is a relative file name such as report.md or data/table.csv; an existing file is replaced.",
            new { path = new { type = "string" }, content = new { type = "string" } }, ["path", "content"]),
        tool("list_outputs", "List files already saved in this job's output folder.", new { }, []),
    ];

    internal DesktopToolResult Call(string tool, IReadOnlyDictionary<string, string> arguments, AgentAccessMode access)
    {
        if (tool == "list_outputs") return Ok(JsonSerializer.Serialize(new { files = List() }));
        if (access == AgentAccessMode.Preview) return Fail("Preview mode: saving files is disabled for this task.");
        if (arguments.Count != 2 || !arguments.TryGetValue("path", out var path) || !arguments.TryGetValue("content", out var content))
            return Fail("write_output requires only path and content.");
        if (content.Length > MaxFileCharacters) return Fail($"An output file is limited to {MaxFileCharacters} characters.");
        var full = Resolve(path);
        if (full is null) return Fail("Use a relative file name inside the output folder, without .. segments or executable/script extensions.");
        if (!File.Exists(full) && List().Count >= MaxFiles) return Fail($"A job can keep up to {MaxFiles} output files.");
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporary, full, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return Ok($"Saved output/{Path.GetRelativePath(_root, full).Replace('\\', '/')} ({new FileInfo(full).Length} bytes).");
    }

    internal IReadOnlyList<string> List() =>
        Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
            .Where(file => !file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            .Select(file => Path.GetRelativePath(_root, file).Replace('\\', '/')).Order(StringComparer.OrdinalIgnoreCase).Take(MaxFiles).ToArray();

    internal string? Resolve(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Length > 200 || Path.IsPathRooted(relative) || relative.Contains(':')) return null;
        var parts = relative.Replace('\\', '/').Split('/');
        if (parts.Length > 4 || parts.Any(part => part is "" or "." or ".." || part.Trim() != part || part.EndsWith('.')
                || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            return null;
        var extension = Path.GetExtension(parts[^1]);
        if (extension.Length == 0 || BlockedExtensions.Contains(extension) || extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase)) return null;
        var full = Path.GetFullPath(Path.Combine(_root, Path.Combine(parts)));
        return full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    private static DesktopToolResult Ok(string text) => new(false, [new("text", text)]);
    private static DesktopToolResult Fail(string text) => new(true, [new("text", text)]);
}
