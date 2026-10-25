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
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenClicky", "settings.json");
        Assert.NotNull(BrowserUploadPolicy.Validate([path]));
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
