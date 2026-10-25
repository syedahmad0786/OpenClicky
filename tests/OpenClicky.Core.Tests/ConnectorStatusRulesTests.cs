using OpenClicky.Core.Settings;
using OpenClicky.Core.Shell;

namespace OpenClicky.Core.Tests;

public class ConnectorStatusRulesTests
{
    private static ConnectorInfo Info(ConnectorAuth auth, bool signedIn = false) =>
        new("k", "K", "h.test", DateTimeOffset.UnixEpoch) { Auth = auth, SignedIn = signedIn };

    [Fact]
    public void SigningInWins()
    {
        var check = new ConnectorCheck(ConnectorStatus.Connected, 3, "");
        Assert.Equal(ConnectorStatus.SigningIn, ConnectorStatusRules.Display(Info(ConnectorAuth.Browser), check, true));
        Assert.Equal(ConnectorStatus.SigningIn, ConnectorStatusRules.Display(Info(ConnectorAuth.None), null, true));
    }

    [Fact]
    public void ACheckWinsOverTheStoredState()
    {
        var check = new ConnectorCheck(ConnectorStatus.Reconnect, null, "expired");
        Assert.Equal(ConnectorStatus.Reconnect, ConnectorStatusRules.Display(Info(ConnectorAuth.Browser), check, false));
        Assert.Equal(ConnectorStatus.Connected, ConnectorStatusRules.Display(Info(ConnectorAuth.Browser), new(ConnectorStatus.Connected, 2, ""), false));
    }

    [Fact]
    public void BrowserAuthNotSignedInNeedsSignIn()
    {
        Assert.Equal(ConnectorStatus.NeedsSignIn, ConnectorStatusRules.Display(Info(ConnectorAuth.Browser), null, false));
        Assert.Equal(ConnectorStatus.Configured, ConnectorStatusRules.Display(Info(ConnectorAuth.Browser, signedIn: true), null, false));
    }

    [Theory]
    [InlineData(ConnectorAuth.None)]
    [InlineData(ConnectorAuth.ApiKey)]
    public void UncheckedOthersAreConfigured(ConnectorAuth auth) =>
        Assert.Equal(ConnectorStatus.Configured, ConnectorStatusRules.Display(Info(auth), null, false));

    [Fact]
    public void OAuthFailuresCarryTheirCopy()
    {
        var ex = new McpOAuthException(McpOAuthFailure.PortInUse);
        Assert.Equal(McpOAuthFailure.PortInUse, ex.Failure);
        Assert.Equal("Another app is using port 47613, which sign-in needs. Close it and try again.", ex.Message);
        Assert.Equal("Your sign-in has expired. Reconnect to keep using it.", new McpOAuthException(McpOAuthFailure.RefreshRejected).Message);
        foreach (var failure in Enum.GetValues<McpOAuthFailure>())
            Assert.False(string.IsNullOrWhiteSpace(new McpOAuthException(failure).Message));
    }
}
