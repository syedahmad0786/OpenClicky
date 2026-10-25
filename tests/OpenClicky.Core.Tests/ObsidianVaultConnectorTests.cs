using System.Diagnostics;
using System.Text.Json;
using OpenClicky.Providers;
using Xunit;

namespace OpenClicky.Core.Tests;

public sealed class ObsidianVaultConnectorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "oc-vault-" + Guid.NewGuid().ToString("N"));
    private readonly string _vault;

    public ObsidianVaultConnectorTests()
    {
        _vault = Path.Combine(_root, "vault");
        Directory.CreateDirectory(Path.Combine(_vault, ".obsidian"));
        Directory.CreateDirectory(Path.Combine(_vault, "Daily"));
        File.WriteAllText(Path.Combine(_vault, ".obsidian", "app.json"), "{}");
        File.WriteAllText(Path.Combine(_vault, "Daily", "2026-10-25.md"), "# Today\nDeploy to Kubernetes after lunch.\n");
        File.WriteAllText(Path.Combine(_vault, "Ideas.md"), "# Ideas\nnothing here\n");
        File.WriteAllText(Path.Combine(_vault, "image.png"), "not a note");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private async Task<(bool IsError, JsonElement Json, string Text)> Call(string tool, object arguments)
    {
        using var connector = new ObsidianVaultConnector(_vault);
        var arg = JsonSerializer.SerializeToElement(arguments);
        var result = await connector.CallToolAsync(tool, arg, CancellationToken.None);
        JsonElement json = default;
        if (!result.IsError) json = JsonDocument.Parse(result.Text).RootElement.Clone();
        return (result.IsError, json, result.Text);
    }

    private static string[] Strings(JsonElement array) => array.EnumerateArray().Select(e => e.GetString()!).ToArray();

    [Fact]
    public async Task ListsFourToolsWithReadOnlyHints()
    {
        using var connector = new ObsidianVaultConnector(_vault);
        Assert.Equal("obsidian", connector.Key);
        Assert.Equal("Obsidian", connector.DisplayName);
        var tools = await connector.ListToolsAsync(CancellationToken.None);
        Assert.Equal(ObsidianVaultConnector.ToolCount, tools.Count);
        Assert.Equal(["list_notes", "read_note", "search_notes", "write_note"], tools.Select(t => t.Name).Order().ToArray());
        Assert.True(tools.Single(t => t.Name == "list_notes").ReadOnly);
        Assert.True(tools.Single(t => t.Name == "search_notes").ReadOnly);
        Assert.True(tools.Single(t => t.Name == "read_note").ReadOnly);
        Assert.False(tools.Single(t => t.Name == "write_note").ReadOnly);
        Assert.All(tools, t => Assert.Equal("object", JsonDocument.Parse(t.InputSchemaJson).RootElement.GetProperty("type").GetString()));
        Assert.Equal("Read one note by its path from list_notes or search_notes.", tools.Single(t => t.Name == "read_note").Description);
    }

    [Fact]
    public async Task ListAndRead()
    {
        var all = await Call("list_notes", new { });
        Assert.False(all.IsError);
        Assert.Equal(["Daily/2026-10-25.md", "Ideas.md"], Strings(all.Json.GetProperty("notes")));

        var daily = await Call("list_notes", new { folder = "Daily" });
        Assert.Equal(["Daily/2026-10-25.md"], Strings(daily.Json.GetProperty("notes")));

        var read = await Call("read_note", new { path = "Ideas.md" });
        Assert.False(read.IsError);
        Assert.Equal("# Ideas\nnothing here\n", read.Json.GetProperty("content").GetString());
    }

    [Fact]
    public async Task ReadCutsLongNotes()
    {
        File.WriteAllText(Path.Combine(_vault, "Long.md"), new string('x', 250_000));
        var read = await Call("read_note", new { path = "Long.md" });
        var content = read.Json.GetProperty("content").GetString()!;
        Assert.EndsWith("\n[cut: the note is longer]", content);
        Assert.Equal(200_000 + "\n[cut: the note is longer]".Length, content.Length);
    }

    [Fact]
    public async Task SearchFindsLines()
    {
        var found = await Call("search_notes", new { query = "KUBERNETES" });
        var hit = Assert.Single(found.Json.GetProperty("results").EnumerateArray());
        Assert.Equal("Daily/2026-10-25.md", hit.GetProperty("path").GetString());
        Assert.Equal(2, hit.GetProperty("line").GetInt32());
        Assert.Contains("Kubernetes", hit.GetProperty("text").GetString());

        var byName = await Call("search_notes", new { query = "ideas" });
        Assert.Contains(byName.Json.GetProperty("results").EnumerateArray(), r => r.GetProperty("path").GetString() == "Ideas.md");

        File.WriteAllText(Path.Combine(_vault, "Many.md"), string.Join('\n', Enumerable.Range(0, 50).Select(i => "needle " + i)));
        var many = await Call("search_notes", new { query = "needle" });
        Assert.Equal(20, many.Json.GetProperty("results").GetArrayLength());

        Assert.True((await Call("search_notes", new { query = "" })).IsError);
        Assert.True((await Call("search_notes", new { query = new string('q', 201) })).IsError);
    }

    [Fact]
    public async Task WriteCreatesFolders()
    {
        var written = await Call("write_note", new { path = "Projects/New.md", content = "# New" });
        Assert.False(written.IsError);
        Assert.Equal("# New", File.ReadAllText(Path.Combine(_vault, "Projects", "New.md")));
        Assert.Empty(Directory.GetFiles(Path.Combine(_vault, "Projects"), "*.tmp"));

        var replaced = await Call("write_note", new { path = "Projects/New.md", content = "# Newer" });
        Assert.False(replaced.IsError);
        Assert.Equal("# Newer", File.ReadAllText(Path.Combine(_vault, "Projects", "New.md")));

        Assert.True((await Call("write_note", new { path = "Big.md", content = new string('x', 1_000_001) })).IsError);
        Assert.False(File.Exists(Path.Combine(_vault, "Big.md")));
    }

    public static IEnumerable<object[]> BadPaths() =>
        new[]
        {
            "../x.md", @"C:\x.md", "/x.md", ".obsidian/app.json", ".obsidian/x.md", "a/.hidden/x.md", "image.png", "x.txt",
            "a/../b.md", "a/b/c/d/e/f/g/h/i.md", "con.md", "a:b.md", "", "a//b.md", @"..\x.md", "x.md.", "x.md ",
        }.Select(p => new object[] { p });

    [Theory]
    [MemberData(nameof(BadPaths))]
    public async Task RefusesEscapes(string path)
    {
        Directory.CreateDirectory(Path.Combine(_root, "outside"));
        File.WriteAllText(Path.Combine(_root, "x.md"), "outside");
        string[] Snapshot() => Directory.GetFileSystemEntries(_root, "*", SearchOption.AllDirectories).Order().ToArray();
        var before = Snapshot();
        var contents = File.ReadAllText(Path.Combine(_root, "x.md"));

        Assert.True((await Call("read_note", new { path })).IsError);
        Assert.True((await Call("write_note", new { path, content = "pwned" })).IsError);

        Assert.Equal(before, Snapshot());
        Assert.Equal(contents, File.ReadAllText(Path.Combine(_root, "x.md")));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(_vault, ".obsidian", "app.json")));
    }

    [Fact]
    public async Task RefusesBadFoldersAndMissingArguments()
    {
        Assert.True((await Call("list_notes", new { folder = ".." })).IsError);
        Assert.True((await Call("list_notes", new { folder = ".obsidian" })).IsError);
        Assert.True((await Call("read_note", new { })).IsError);
        Assert.True((await Call("write_note", new { path = "a.md" })).IsError);
        Assert.True((await Call("nope", new { })).IsError);
    }

    [Fact]
    public async Task SkipsReparsePoints()
    {
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.md"), "top secret kubernetes");
        var link = Path.Combine(_vault, "Linked");
        var mk = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{outside}\"")
        {
            CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
        })!;
        mk.WaitForExit();
        if (mk.ExitCode != 0 || !Directory.Exists(link)) return; // junctions unavailable here: nothing to prove
        try
        {
            var list = await Call("list_notes", new { });
            Assert.DoesNotContain("secret", list.Text);
            var search = await Call("search_notes", new { query = "secret" });
            Assert.Empty(search.Json.GetProperty("results").EnumerateArray());
            Assert.True((await Call("read_note", new { path = "Linked/secret.md" })).IsError);
            Assert.True((await Call("write_note", new { path = "Linked/new.md", content = "x" })).IsError);
            Assert.True((await Call("list_notes", new { folder = "Linked" })).IsError);
            Assert.False(File.Exists(Path.Combine(outside, "new.md")));
        }
        finally
        {
            Directory.Delete(link); // non-recursive: removes only the junction, never its target
        }
    }

    [Fact]
    public void IsVaultNeedsObsidianFolder()
    {
        Assert.True(ObsidianVaultConnector.IsVault(_vault));
        var plain = Path.Combine(_root, "plain");
        Directory.CreateDirectory(plain);
        Assert.False(ObsidianVaultConnector.IsVault(plain));
        Assert.False(ObsidianVaultConnector.IsVault(Path.Combine(_root, "missing")));
        Assert.False(ObsidianVaultConnector.IsVault(null));
        Assert.False(ObsidianVaultConnector.IsVault(""));
    }
}
