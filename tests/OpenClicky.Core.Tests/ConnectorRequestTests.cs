using OpenClicky.Core.Settings;

namespace OpenClicky.Core.Tests;

public class ConnectorRequestTests
{
    [Fact]
    public void CatalogAccountNeedsOnlyTheAppAndALabel()
    {
        Assert.True(ConnectorRequest.TryParse("""{"app":"linear","label":"Work"}""", out var request, out _));
        Assert.Equal(new CatalogAccount(ConnectorCatalog.Find("linear")!, "Work"), request);
        Assert.True(ConnectorRequest.TryParse("""{"app":"deepwiki"}""", out request, out _));
        Assert.Equal(new CatalogAccount(ConnectorCatalog.Find("deepwiki")!, null), request);
        Assert.True(ConnectorRequest.TryParse("""{"app":"github","label":""}""", out request, out _));
        Assert.Null(((CatalogAccount)request!).Label);
    }

    [Fact]
    public void UnknownAppAndBadLabelFail()
    {
        Assert.False(ConnectorRequest.TryParse("""{"app":"nope"}""", out var request, out var error));
        Assert.Null(request);
        Assert.Equal("That app isn't in the list.", error);
        var longLabel = new string('x', 31);
        Assert.False(ConnectorRequest.TryParse($$"""{"app":"linear","label":"{{longLabel}}"}""", out _, out error));
        Assert.Equal("Keep the label to 30 characters.", error);
    }

    [Fact]
    public void ParsesCustomRemote()
    {
        Assert.True(ConnectorRequest.TryParse(
            """{"name":" My server ","kind":"remote","url":"https://x.test/mcp","auth":"browser","clientId":"abc","headers":"X-A: 1"}""", out var request, out _));
        var remote = Assert.IsType<CustomRemote>(request);
        Assert.Equal("My server", remote.Name);
        Assert.Equal("https://x.test/mcp", remote.Url);
        Assert.Equal(ConnectorAuth.Browser, remote.Auth);
        Assert.Equal("abc", remote.ClientId);
        Assert.Equal("1", remote.Headers["X-A"]);
        Assert.Null(remote.App);
    }

    [Fact]
    public void KindDefaultsToRemoteAndClientIdOnlyForBrowser()
    {
        Assert.True(ConnectorRequest.TryParse("""{"name":"S","url":"https://x.test/mcp","auth":"apiKey","clientId":"abc"}""", out var request, out _));
        var remote = Assert.IsType<CustomRemote>(request);
        Assert.Equal(ConnectorAuth.ApiKey, remote.Auth);
        Assert.Null(remote.ClientId);
        Assert.Empty(remote.Headers);
    }

    [Fact]
    public void ParsesCustomLocal()
    {
        Assert.True(ConnectorRequest.TryParse(
            """{"name":"Files","kind":"local","command":"npx","args":"-y\n@scope/server","env":"TOKEN=1"}""", out var request, out _));
        var local = Assert.IsType<CustomLocal>(request);
        Assert.Equal("Files", local.Name);
        Assert.Equal("npx", local.Command);
        Assert.Equal(["-y", "@scope/server"], local.Arguments);
        Assert.Equal("1", local.Environment["TOKEN"]);
    }

    [Fact]
    public void LocalIgnoresAuthAndRejectsBadFields()
    {
        Assert.True(ConnectorRequest.TryParse("""{"name":"F","kind":"local","command":"npx","auth":"oauth"}""", out var request, out _));
        Assert.IsType<CustomLocal>(request);
        Assert.False(ConnectorRequest.TryParse("""{"name":"F","kind":"local","command":""}""", out _, out var error));
        Assert.Equal("Give the command to run, like npx.", error);
        Assert.False(ConnectorRequest.TryParse("""{"name":"F","kind":"local","command":"npx","env":"1BAD=x"}""", out _, out error));
        Assert.Equal("Write each variable as NAME=value on its own line.", error);
        Assert.False(ConnectorRequest.TryParse("""{"name":"F","kind":"sideways","url":"https://x.test/mcp","auth":"none"}""", out _, out _));
    }

    [Fact]
    public void ComposioIsACustomRemoteWithTheAppSet()
    {
        Assert.True(ConnectorRequest.TryParse(
            """{"app":"composio","name":"Composio","kind":"remote","url":"https://backend.composio.dev/v3/mcp/x","auth":"apiKey"}""", out var request, out _));
        var remote = Assert.IsType<CustomRemote>(request);
        Assert.Equal("composio", remote.App);
        Assert.Equal(ConnectorAuth.ApiKey, remote.Auth);
    }

    [Fact]
    public void ObsidianIsTheVaultSetup()
    {
        Assert.True(ConnectorRequest.TryParse("""{"app":"obsidian"}""", out var request, out _));
        Assert.IsType<VaultSetup>(request);
    }

    [Fact]
    public void HeaderRulesFollowTheAuth()
    {
        Assert.False(ConnectorRequest.TryParse(
            """{"name":"S","url":"https://x.test/mcp","auth":"apiKey","headers":"Authorization: Bearer x"}""", out _, out var error));
        Assert.Equal("Header Authorization can't be set here.", error);
        Assert.True(ConnectorRequest.TryParse(
            """{"name":"S","url":"https://x.test/mcp","auth":"none","headers":"Authorization: Bearer x"}""", out _, out _));
    }

    [Fact]
    public void TrimsNamesAndAcceptsLocalHttp()
    {
        Assert.True(ConnectorRequest.TryParse("""{"name":" Linear ","url":"https://mcp.linear.app/sse","auth":"apiKey"}""", out var request, out _));
        var remote = Assert.IsType<CustomRemote>(request);
        Assert.Equal("Linear", remote.Name);
        Assert.Equal("https://mcp.linear.app/sse", remote.Url);
        Assert.True(ConnectorRequest.TryParse("""{"name":"Local","url":"http://127.0.0.1:3000/mcp","auth":"none"}""", out request, out _));
        Assert.Equal(ConnectorAuth.None, ((CustomRemote)request!).Auth);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"name":"","url":"https://x.example","auth":"none"}""")]
    [InlineData("""{"name":"X","url":"http://evil.example/mcp","auth":"none"}""")]
    [InlineData("""{"name":"X","url":"https://user:pw@x.example/mcp","auth":"none"}""")]
    [InlineData("""{"name":"X","url":"https://x.example/mcp","auth":"oauth"}""")]
    [InlineData("""{"name":1,"url":"https://x.example/mcp","auth":"none"}""")]
    [InlineData("""{"url":"https://x.example/mcp","auth":"none"}""")]
    [InlineData("""{"name":"X","url":"https://x.example/mcp"}""")]
    [InlineData("""{"app":1}""")]
    public void RejectsBadRequests(string? arg)
    {
        Assert.False(ConnectorRequest.TryParse(arg, out var request, out var error));
        Assert.Null(request);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void RejectsOversizeAndLongNames()
    {
        var longName = new string('n', 61);
        Assert.False(ConnectorRequest.TryParse($$"""{"name":"{{longName}}","url":"https://x.example/mcp","auth":"none"}""", out _, out _));
        Assert.True(ConnectorRequest.TryParse($$"""{"name":"{{new string('n', 60)}}","url":"https://x.example/mcp","auth":"none"}""", out _, out _));
        var huge = $$"""{"name":"X","url":"https://x.example/{{new string('p', ConnectorRequest.MaxArg)}}","auth":"none"}""";
        Assert.False(ConnectorRequest.TryParse(huge, out _, out _));
        // The old 512-char limit no longer applies to connector.add.
        var long600 = $$"""{"name":"X","url":"https://x.example/{{new string('p', 600)}}","auth":"none"}""";
        Assert.True(ConnectorRequest.TryParse(long600, out _, out _));
    }

    [Fact]
    public void ErrorsDropTheParameterSuffix()
    {
        Assert.False(ConnectorRequest.TryParse("""{"name":"X","url":"http://evil.example/mcp","auth":"none"}""", out _, out var error));
        Assert.DoesNotContain("(Parameter", error);
    }
}
