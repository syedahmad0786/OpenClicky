using OpenClicky.Core.Settings;
using Xunit;

namespace OpenClicky.Core.Tests;

/// <summary>The one proxy rule shared by the notch (SettingsPatch) and the classic form (SettingsForm).</summary>
public sealed class ProxyRuleTests
{
    [Theory]
    [InlineData("http://127.0.0.1:8080", "http://127.0.0.1:8080")]
    [InlineData(" https://localhost/ ", "https://localhost")]
    [InlineData("http://[::1]:9000", "http://[::1]:9000")]
    public void LoopbackAddressesAreAccepted(string text, string expected) => Assert.Equal(expected, SettingsCatalog.LoopbackUrl(text));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("http://evil.example.com:8080")]
    [InlineData("http://user:pw@127.0.0.1")]
    [InlineData("ftp://127.0.0.1")]
    [InlineData("127.0.0.1:8080")]
    public void EverythingElseIsRejected(string? text) => Assert.Null(SettingsCatalog.LoopbackUrl(text));
}
