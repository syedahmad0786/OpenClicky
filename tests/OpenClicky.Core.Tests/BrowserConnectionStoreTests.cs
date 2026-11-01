using OpenClicky.Core.Browser;
using Xunit;

namespace OpenClicky.Core.Tests;

public sealed class BrowserConnectionStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "oc-bcs-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "sub", "connection.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void MissingFileGivesDefaults() =>
        Assert.Equal(new BrowserConnection(), BrowserConnectionStore.Load(FilePath));

    [Fact]
    public void RoundTrips()
    {
        var value = new BrowserConnection("edge", "Profile 2", "inst-1", true);
        BrowserConnectionStore.Save(FilePath, value);
        Assert.Equal(value, BrowserConnectionStore.Load(FilePath));
        var bytes = File.ReadAllBytes(FilePath);
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Contains("\"profileDirectory\"", System.Text.Encoding.UTF8.GetString(bytes));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(FilePath)!, "*.tmp"));
    }

    [Fact]
    public void BrokenFileGivesDefaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, "{ nope");
        Assert.Equal(new BrowserConnection(), BrowserConnectionStore.Load(FilePath));
    }
}
