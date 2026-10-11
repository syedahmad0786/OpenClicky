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
    }
}
