using OpenClicky.Core.Browser;

namespace OpenClicky.Core.Tests;

public sealed class BrowserUploadPolicyTests : IDisposable
{
    private readonly string _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "openclicky-upload-" + Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string File(string name, long length = 4)
    {
        var path = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = System.IO.File.Create(path);
        stream.SetLength(length);
        return path;
    }

    [Fact]
    public void AcceptsOrdinaryFile()
    {
        Assert.Null(BrowserUploadPolicy.Validate([File("report.pdf"), File("photo.jpg")]));
    }

    [Fact]
    public void RefusesRelativeMissingAndDirectory()
    {
        Assert.NotNull(BrowserUploadPolicy.Validate([]));
        Assert.NotNull(BrowserUploadPolicy.Validate(["report.pdf"]));
        Assert.NotNull(BrowserUploadPolicy.Validate([@"..\report.pdf"]));
        Assert.NotNull(BrowserUploadPolicy.Validate([Path.Combine(_dir, "missing.pdf")]));
        Assert.NotNull(BrowserUploadPolicy.Validate([_dir]));
    }

    [Theory]
    [InlineData(".env")]
    [InlineData(".env.local")]
    [InlineData(".netrc")]
    [InlineData(".git-credentials")]
    [InlineData(".npmrc")]
    [InlineData(".pypirc")]
    [InlineData(".pgpass")]
    [InlineData("id_rsa")]
    [InlineData("id_ed25519")]
    [InlineData("server.pem")]
    [InlineData("tls.KEY")]
    [InlineData("cert.p12")]
    [InlineData("cert.pfx")]
    [InlineData("vault.kdbx")]
    [InlineData(@"Chrome\User Data\Default\Login Data")]
    public void RefusesSecretNames(string name)
    {
        Assert.NotNull(BrowserUploadPolicy.Validate([File(name)]));
    }

    [Fact]
    public void RefusesOpenClickyAppData()
    {
        var root = Path.Combine(_dir, "AppData", "OpenClicky");
        var settings = File(Path.Combine("AppData", "OpenClicky", "settings.json"));

        Assert.Equal("settings.json looks like a secret or browser data, so it was not uploaded.",
            BrowserUploadPolicy.Validate([settings], [root]));
        Assert.Null(BrowserUploadPolicy.Validate([File(Path.Combine("AppData", "OpenClickyNotes", "notes.txt"))], [root]));
        Assert.Contains(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenClicky"),
            BrowserUploadPolicy.DefaultProtectedRoots);
    }

    [Fact]
    public void RefusesStreamDeviceAndUncSpellings()
    {
        var plain = File("notes.txt");
        Assert.Null(BrowserUploadPolicy.Validate([plain]));
        Assert.NotNull(BrowserUploadPolicy.Validate([plain + "::$DATA"]));
        Assert.NotNull(BrowserUploadPolicy.Validate([plain + ":hidden"]));
        Assert.NotNull(BrowserUploadPolicy.Validate([@"\\?\" + plain]));
        Assert.NotNull(BrowserUploadPolicy.Validate([@"\\.\" + plain]));
        Assert.NotNull(BrowserUploadPolicy.Validate(["//?/" + plain]));
        Assert.NotNull(BrowserUploadPolicy.Validate([@"\\localhost\" + plain[0] + "$" + plain[2..]]));
    }

    [Fact]
    public void RefusesProtectedFileReachedThroughJunction()
    {
        var root = Path.Combine(_dir, "protected", "OpenClicky");
        File(Path.Combine("protected", "OpenClicky", "notes.txt"));
        var link = Path.Combine(_dir, "link");
        if (!TryLinkDirectory(link, root)) return; // no symlink privilege and no mklink: nothing to test here
        try
        {
            Assert.Equal("notes.txt looks like a secret or browser data, so it was not uploaded.",
                BrowserUploadPolicy.Validate([Path.Combine(link, "notes.txt")], [root]));
        }
        finally { Directory.Delete(link); } // the link only, never what it points to
    }

    private static bool TryLinkDirectory(string link, string target)
    {
        try { Directory.CreateSymbolicLink(link, target); return true; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        if (!OperatingSystem.IsWindows()) return false;
        using var mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
        mklink.WaitForExit(10_000);
        return Directory.Exists(link);
    }

    [Fact]
    public void RefusesOverHundredMegabytes()
    {
        Assert.Null(BrowserUploadPolicy.Validate([File("exact.bin", 100L * 1024 * 1024)]));
        Assert.NotNull(BrowserUploadPolicy.Validate([File("big.bin", 100L * 1024 * 1024 + 1)]));
    }

    [Fact]
    public void RefusesMoreThanTen()
    {
        var ten = Enumerable.Range(0, 10).Select(i => File($"f{i}.txt")).ToList();
        Assert.Null(BrowserUploadPolicy.Validate(ten));
        Assert.NotNull(BrowserUploadPolicy.Validate([.. ten, File("f10.txt")]));
    }
}
