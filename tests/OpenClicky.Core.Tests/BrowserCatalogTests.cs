using OpenClicky.Core.Browser;
using Xunit;

namespace OpenClicky.Core.Tests;

public sealed class BrowserCatalogTests
{
    private const string Pf = @"C:\Program Files";
    private const string Pf86 = @"C:\Program Files (x86)";
    private const string Local = @"C:\Users\t\AppData\Local";

    private sealed class FakeProbe(Dictionary<string, string?> files, string? appPath = null) : IBrowserProbe
    {
        public string LocalAppData => Local;
        public string ProgramFiles => Pf;
        public string ProgramFilesX86 => Pf86;
        public bool FileExists(string path) => files.ContainsKey(path);
        public string? ReadText(string path) => files.TryGetValue(path, out var t) ? t : null;
        public string? AppPath(string exeName) => appPath;
    }

    private const string TwoProfiles =
        """{"profile":{"info_cache":{"Profile 2":{"name":"Work"},"Default":{"name":"Ahmad"}}}}""";

    [Fact]
    public void FindsChromeAndEdgeWithProfiles()
    {
        var chromeExe = Path.Combine(Pf, @"Google\Chrome\Application", "chrome.exe");
        var edgeExe = Path.Combine(Pf86, @"Microsoft\Edge\Application", "msedge.exe");
        var state = Path.Combine(Local, @"Google\Chrome\User Data", "Local State");
        var found = BrowserCatalog.Detect(new FakeProbe(new() { [chromeExe] = null, [edgeExe] = null, [state] = TwoProfiles }));

        Assert.Equal(["chrome", "edge"], found.Select(b => b.Kind.Id));
        var chrome = found[0];
        Assert.Equal(chromeExe, chrome.ExePath);
        Assert.Equal(Path.Combine(Local, @"Google\Chrome\User Data"), chrome.UserDataPath);
        Assert.Equal([new BrowserProfile("Default", "Ahmad"), new BrowserProfile("Profile 2", "Work")], chrome.Profiles);
        Assert.Equal([new BrowserProfile("Default", "Default")], found[1].Profiles);
    }

    [Fact]
    public void SkipsMissingBrowsers() =>
        Assert.Empty(BrowserCatalog.Detect(new FakeProbe(new())));

    [Fact]
    public void CanaryLivesInLocalAppData()
    {
        var exe = Path.Combine(Local, @"Google\Chrome SxS\Application", "chrome.exe");
        var found = Assert.Single(BrowserCatalog.Detect(new FakeProbe(new() { [exe] = null })));
        Assert.Equal("chrome-canary", found.Kind.Id);
        Assert.Equal(exe, found.ExePath);
    }

    [Fact]
    public void AppPathsEntryCountsWhenFolderMatches()
    {
        // Not under any root and the folder does not match the install dir: ignored.
        var other = @"D:\Apps\Brave\Application\brave.exe";
        Assert.Empty(BrowserCatalog.Detect(new FakeProbe(new() { [other] = null }, other)));
        var good = @"D:\Apps\BraveSoftware\Brave-Browser\Application\brave.exe";
        var found = Assert.Single(BrowserCatalog.Detect(new FakeProbe(new() { [good] = null }, good)));
        Assert.Equal(("brave", good), (found.Kind.Id, found.ExePath));
    }

    [Fact]
    public void ParseProfilesSkipsSystemAndGuest()
    {
        var json = """{"profile":{"info_cache":{"System Profile":{"name":"S"},"Guest Profile":{"name":"G"},"Profile 1":{},"Default":{"name":"Me"}}}}""";
        Assert.Equal([new BrowserProfile("Default", "Me"), new BrowserProfile("Profile 1", "Profile 1")],
            BrowserCatalog.ParseProfiles(json));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{not json")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("5")]
    [InlineData("""{"profile":5}""")]
    [InlineData("""{"profile":[]}""")]
    [InlineData("""{"profile":{"info_cache":[]}}""")]
    public void BrokenLocalStateGivesDefault(string? json) =>
        Assert.Equal([new BrowserProfile("Default", "Default")], BrowserCatalog.ParseProfiles(json));

    [Fact]
    public void KnownOrderIsStable() =>
        Assert.Equal(["chrome", "chrome-beta", "chrome-dev", "chrome-canary", "chromium", "edge", "brave", "vivaldi"],
            BrowserCatalog.Known.Select(k => k.Id));
}
