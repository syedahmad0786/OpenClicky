using System.Diagnostics;
using System.Text.Json;
using OpenClicky.Core;
using OpenClicky.Core.Settings;
using OpenClicky.Core.Shell;
using OpenClicky.Providers;

namespace OpenClicky.Core.Tests;

public class ConnectorVerifierTests
{
    private static readonly TimeSpan Thirty = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task CountsTools()
    {
        Assert.Equal(new ConnectorCheck(ConnectorStatus.Connected, 2, "2 tools available."), await Check(Tools(2)));
        Assert.Equal(new ConnectorCheck(ConnectorStatus.Connected, 1, "1 tool available."), await Check(Tools(1)));
    }

    [Fact]
    public async Task RejectedCredentialsMapByAuth()
    {
        Assert.Equal(new ConnectorCheck(ConnectorStatus.TokenRejected, null, "The server rejected the key."), await Check(Http(401), ConnectorAuth.ApiKey));
        Assert.Equal(new ConnectorCheck(ConnectorStatus.Reconnect, null, "Sign in again to keep using it."), await Check(Http(401), ConnectorAuth.Browser));
        Assert.Equal(new ConnectorCheck(ConnectorStatus.NeedsSignIn, null, "This server needs you to sign in or add a key."), await Check(Http(403), ConnectorAuth.None));
        Assert.Equal(new ConnectorCheck(ConnectorStatus.NeedsSignIn, null, "This server needs you to sign in or add a key."), await Check(Http(401), ConnectorAuth.None));
        var failed = await Check(Http(500), ConnectorAuth.ApiKey);
        Assert.Equal(ConnectorStatus.Error, failed.Status);
        Assert.Null(failed.ToolCount);
        Assert.Equal("Fake answered HTTP 500.", failed.Message);
    }

    [Fact]
    public async Task HangPastTimeoutIsError()
    {
        var clock = Stopwatch.StartNew();
        var check = await ConnectorVerifier.CheckAsync(new Fake(_ => new TaskCompletionSource<IReadOnlyList<RemoteMcpTool>>().Task), ConnectorAuth.None,
            TimeSpan.FromMilliseconds(100), CancellationToken.None);
        Assert.Equal(ConnectorStatus.Error, check.Status);
        Assert.StartsWith("It did not answer within", check.Message);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5));

        var named = await ConnectorVerifier.CheckAsync(new Fake(async token => { await Task.Delay(Timeout.Infinite, token); return []; }), ConnectorAuth.None,
            TimeSpan.FromMilliseconds(1_500), CancellationToken.None);
        Assert.Equal(new ConnectorCheck(ConnectorStatus.Error, null, "It did not answer within 2 seconds."), named);
    }

    [Fact]
    public async Task CallerCancellationIsNotATimeout()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ConnectorVerifier.CheckAsync(new Fake(async token => { await Task.Delay(Timeout.Infinite, token); return []; }), ConnectorAuth.None, Thirty, cancelled.Token));
    }

    [Fact]
    public async Task RefreshRejectedIsReconnect()
    {
        var refresh = new McpOAuthException(McpOAuthFailure.RefreshRejected);
        Assert.Equal(new ConnectorCheck(ConnectorStatus.Reconnect, null, refresh.Message), await Check(_ => throw refresh, ConnectorAuth.Browser));
        var other = new McpOAuthException(McpOAuthFailure.NoMetadata);
        Assert.Equal(new ConnectorCheck(ConnectorStatus.NeedsSignIn, null, other.Message), await Check(_ => throw other, ConnectorAuth.Browser));
    }

    private static Task<ConnectorCheck> Check(Func<CancellationToken, Task<IReadOnlyList<RemoteMcpTool>>> list, ConnectorAuth auth = ConnectorAuth.None) =>
        ConnectorVerifier.CheckAsync(new Fake(list), auth, Thirty, CancellationToken.None);

    private static Func<CancellationToken, Task<IReadOnlyList<RemoteMcpTool>>> Tools(int count) =>
        _ => Task.FromResult<IReadOnlyList<RemoteMcpTool>>(Enumerable.Range(0, count).Select(i => new RemoteMcpTool("t" + i, "", true, "{}")).ToList());

    private static Func<CancellationToken, Task<IReadOnlyList<RemoteMcpTool>>> Http(int status) =>
        _ => throw new McpHttpException(status, $"Fake answered HTTP {status}.");

    private sealed class Fake(Func<CancellationToken, Task<IReadOnlyList<RemoteMcpTool>>> list) : McpConnector("fake", "Fake")
    {
        public override Task<IReadOnlyList<RemoteMcpTool>> ListToolsAsync(CancellationToken token) => list(token);
        public override Task<RemoteMcpCallResult> CallToolAsync(string name, JsonElement arguments, CancellationToken token) => throw new NotSupportedException();
        public override void Dispose() { }
    }
}
