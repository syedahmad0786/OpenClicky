namespace OpenClicky.Core.Tests;

public class AgentDenyListTests
{
    [Theory]
    [InlineData("1Password")]
    [InlineData("Bitwarden - Vault")]
    [InlineData("Chase Banking")]
    [InlineData("Coinbase")]
    public void DeniesSensitiveTitles(string title)
    {
        Assert.True(AgentDenyList.IsDenied(title));
        Assert.NotNull(AgentDenyList.MatchReason(title));
    }

    [Theory]
    [InlineData("Notepad")]
    [InlineData("Visual Studio Code")]
    [InlineData(null)]
    [InlineData("")]
    public void AllowsNormalTitles(string? title)
    {
        Assert.False(AgentDenyList.IsDenied(title));
    }

    [Fact]
    public void ExtraFragmentsWork()
    {
        Assert.True(AgentDenyList.IsDenied("Acme Payroll Portal", ["payroll"]));
        Assert.Equal("payroll", AgentDenyList.MatchReason("Acme Payroll Portal", ["payroll"]));
    }

    [Fact]
    public void ParseExtraFragments_SplitsCsv()
    {
        var parts = AgentDenyList.ParseExtraFragments(" payroll , vault\nhr portal ");
        Assert.Equal(3, parts.Count);
        Assert.Contains("payroll", parts);
        Assert.Contains("hr portal", parts);
    }

    [Fact]
    public void FragmentsAreDefaultsThenTrimmedLowerExtras()
    {
        var all = AgentDenyList.Fragments(["  Payroll ", "", "PayPal"]);
        Assert.Equal("1password", all[0]);
        Assert.Equal("payroll", all[^1]);
        Assert.Single(all, f => f == "paypal");
        Assert.All(all, f => Assert.Equal(f.Trim().ToLowerInvariant(), f));
    }
}
