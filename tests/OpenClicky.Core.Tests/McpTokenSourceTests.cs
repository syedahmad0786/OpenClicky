using System.Net;
using OpenClicky.Core;
using OpenClicky.Providers;

namespace OpenClicky.Core.Tests;

public class McpTokenSourceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public async Task RefreshesNearExpiry()
    {
        var gate = new TaskCompletionSource();
        var server = new McpOAuthTests.FakeServer();
        server.Route("POST", "https://as.test/token", _ =>
        {
            gate.Task.Wait(TimeSpan.FromSeconds(10));
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("""{"access_token":"at-2","expires_in":3600}""", System.Text.Encoding.UTF8, "application/json") };
        });
        using var oauth = new McpOAuth(server);
        var saved = new List<McpOAuthTokens>();
        var source = new McpTokenSource(McpOAuthTests.Tokens(Now.AddSeconds(30)), oauth, t => { lock (saved) saved.Add(t); }, () => Now);

        var first = Task.Run(() => source.GetAsync(CancellationToken.None));
        var second = Task.Run(() => source.GetAsync(CancellationToken.None));
        await Task.Delay(100);
        gate.SetResult();

        Assert.Equal(new[] { "at-2", "at-2" }, await Task.WhenAll(first, second));
        Assert.Single(server.Requests);
        var save = Assert.Single(saved);
        Assert.Equal(("at-2", "rt-1"), (save.AccessToken, save.RefreshToken));
        Assert.Equal("at-2", await source.GetAsync(CancellationToken.None));
        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task SaveFailureKeepsRotatedTokens()
    {
        var server = new McpOAuthTests.FakeServer();
        server.Json("POST", "https://as.test/token", """{"access_token":"at-2","refresh_token":"rt-2","expires_in":3600}""");
        using var oauth = new McpOAuth(server);
        var source = new McpTokenSource(McpOAuthTests.Tokens(Now.AddSeconds(10)), oauth, _ => throw new IOException("disk full"), () => Now);

        await Assert.ThrowsAsync<IOException>(() => source.GetAsync(CancellationToken.None));

        Assert.Equal("at-2", await source.GetAsync(CancellationToken.None));
        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task ValidTokenNoRefresh()
    {
        var server = new McpOAuthTests.FakeServer();
        using var oauth = new McpOAuth(server);

        Assert.Equal("at-1", await new McpTokenSource(McpOAuthTests.Tokens(Now.AddMinutes(10)), oauth, _ => Assert.Fail("saved"), () => Now).GetAsync(CancellationToken.None));
        Assert.Equal("at-1", await new McpTokenSource(McpOAuthTests.Tokens(null), oauth, _ => Assert.Fail("saved"), () => Now).GetAsync(CancellationToken.None));
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task RefreshRejectedThrowsReconnect()
    {
        var server = new McpOAuthTests.FakeServer();
        server.Json("POST", "https://as.test/token", """{"error":"invalid_grant"}""", HttpStatusCode.BadRequest);
        using var oauth = new McpOAuth(server);

        var noRefresh = new McpTokenSource(McpOAuthTests.Tokens(Now.AddSeconds(10)) with { RefreshToken = null }, oauth, _ => Assert.Fail("saved"), () => Now);
        Assert.Equal(McpOAuthFailure.RefreshRejected, (await Assert.ThrowsAsync<McpOAuthException>(() => noRefresh.GetAsync(CancellationToken.None))).Failure);
        Assert.Empty(server.Requests);

        var rejected = new McpTokenSource(McpOAuthTests.Tokens(Now.AddSeconds(-5)), oauth, _ => Assert.Fail("saved"), () => Now);
        Assert.Equal(McpOAuthFailure.RefreshRejected, (await Assert.ThrowsAsync<McpOAuthException>(() => rejected.GetAsync(CancellationToken.None))).Failure);
        Assert.Single(server.Requests);
    }
}
