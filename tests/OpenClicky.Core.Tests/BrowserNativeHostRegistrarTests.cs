using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Win32;
using OpenClicky.Providers;
using Xunit;

namespace OpenClicky.Core.Tests;

[SupportedOSPlatform("windows")]
public sealed class BrowserNativeHostRegistrarTests : IDisposable
{
    private readonly string _rootPath = @"Software\OpenClicky.Tests\" + Guid.NewGuid();
    private readonly RegistryKey _root;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "oc-reg-" + Guid.NewGuid().ToString("N"));

    public BrowserNativeHostRegistrarTests()
    {
        _root = Registry.CurrentUser.CreateSubKey(_rootPath);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        _root.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_rootPath, false);
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void ManifestIsExactAndBomFree()
    {
        var path = BrowserNativeHostRegistrar.WriteManifest(Path.Combine(_dir, "bridge"), @"C:\x\host.exe");
        Assert.Equal(Path.Combine(_dir, "bridge", "com.openclicky.chrome_bridge.json"), path);
        var bytes = File.ReadAllBytes(path);
        Assert.NotEqual(0xEF, bytes[0]);
        using var doc = JsonDocument.Parse(bytes);
        var r = doc.RootElement;
        Assert.Equal("com.openclicky.chrome_bridge", r.GetProperty("name").GetString());
        Assert.Equal("OpenClicky browser bridge", r.GetProperty("description").GetString());
        Assert.Equal(@"C:\x\host.exe", r.GetProperty("path").GetString());
        Assert.Equal("stdio", r.GetProperty("type").GetString());
        Assert.Equal(["chrome-extension://demdidhcdmmfbpidanflhknhplklkcja/"],
            r.GetProperty("allowed_origins").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void RegisterThenUnregister()
    {
        const string vendor = @"Software\Google\Chrome";
        var manifest = Path.Combine(_dir, "m.json");
        Assert.False(BrowserNativeHostRegistrar.IsRegistered(_root, vendor, manifest));
        BrowserNativeHostRegistrar.Register(_root, vendor, manifest);
        Assert.True(BrowserNativeHostRegistrar.IsRegistered(_root, vendor, manifest));
        Assert.False(BrowserNativeHostRegistrar.IsRegistered(_root, vendor, manifest + "x"));
        BrowserNativeHostRegistrar.Unregister(_root, vendor);
        Assert.False(BrowserNativeHostRegistrar.IsRegistered(_root, vendor, manifest));
        BrowserNativeHostRegistrar.Unregister(_root, vendor); // already gone: no throw
    }

    [Fact]
    public void UnregisterLeavesOtherHosts()
    {
        const string vendor = @"Software\Microsoft\Edge";
        using (var other = _root.CreateSubKey(vendor + @"\NativeMessagingHosts\com.other.host"))
            other.SetValue("", "other.json");
        BrowserNativeHostRegistrar.Register(_root, vendor, "m.json");
        BrowserNativeHostRegistrar.Unregister(_root, vendor);
        using var kept = _root.OpenSubKey(vendor + @"\NativeMessagingHosts\com.other.host");
        Assert.Equal("other.json", kept?.GetValue("") as string);
    }

    [Fact]
    public void InstallExtensionCopiesOnlyExtensionFiles()
    {
        var src = Path.Combine(_dir, "src");
        var dst = Path.Combine(_dir, "dst");
        Directory.CreateDirectory(Path.Combine(src, "icons"));
        Directory.CreateDirectory(Path.Combine(src, "test"));
        Directory.CreateDirectory(Path.Combine(dst, "icons"));
        foreach (var f in new[] { "manifest.json", "a.js", "b.mjs", "c.html", "README.md", "notes.txt", @"icons\on.png", @"test\x.mjs" })
            File.WriteAllText(Path.Combine(src, f), "new");
        File.WriteAllText(Path.Combine(dst, "old.js"), "x");
        File.WriteAllText(Path.Combine(dst, "old.html"), "x");
        File.WriteAllText(Path.Combine(dst, "a.js"), "stale-content");
        File.WriteAllText(Path.Combine(dst, "keep.txt"), "x");
        File.WriteAllText(Path.Combine(dst, @"icons\old.png"), "x");

        Assert.Equal(dst, BrowserNativeHostRegistrar.InstallExtension(src, dst));

        var got = Directory.GetFiles(dst, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(dst, p)).Order().ToArray();
        Assert.Equal(new[] { "a.js", "b.mjs", "c.html", @"icons\on.png", "keep.txt", "manifest.json" }.Order(), got);
        Assert.Equal("new", File.ReadAllText(Path.Combine(dst, "a.js")));
    }
}
