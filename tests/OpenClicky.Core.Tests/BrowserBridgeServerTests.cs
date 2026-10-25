using System.IO.Pipes;
using System.Text.Json;
using System.Threading.Channels;
using OpenClicky.Core;
using OpenClicky.Providers;

namespace OpenClicky.Core.Tests;

public sealed class BrowserBridgeServerTests
{
    private static readonly TimeSpan T = TimeSpan.FromSeconds(10);
    private static readonly byte[] Secret = Enumerable.Range(7, 32).Select(i => (byte)i).ToArray();
    private static string NewPipe() => "OpenClicky.Test." + Guid.NewGuid().ToString("N");
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
    private static BrowserCommand Cmd(string op, string task = "task-1") => new(op, task, "job-1", "Research", "blue", ["bank"], null, Json("{}"));

    [Fact]
    public async Task HelloSelectsFirstProfileAndRoundTripsCommand()
    {
        var pipe = NewPipe();
        await using var rig = new ServerRig(pipe);
        var offline = await rig.Server.SendAsync(Cmd("tabs"), T, default);
        Assert.Equal(new BrowserReply(false, "The browser isn't connected. Connect it in Settings → Integrations."), offline);

        await using var chrome = new FakeChrome(pipe);
        await chrome.HelloAsync("profile-a");
        var ready = await chrome.ReadyAsync();
        Assert.True(ready.GetProperty("selected").GetBoolean());
        Assert.True(ready.GetProperty("app").GetBoolean());
        Assert.Equal("profile-a", rig.Stored);
        var instance = Assert.Single(rig.Server.Instances);
        Assert.Equal(("profile-a", "Chrome", true), (instance.Id, instance.Brand, instance.Selected));

        var pending = rig.Server.SendAsync(new BrowserCommand("tabs", "task-1", "job-1", "Research", "green", ["bank"], "Looking", Json("""{"x":1}""")), T, default);
        var cmd = await chrome.CommandAsync("tabs");
        Assert.Equal("task-1", cmd.GetProperty("task").GetString());
        Assert.Equal("job-1", cmd.GetProperty("owner").GetString());
        Assert.Equal("Research", cmd.GetProperty("groupTitle").GetString());
        Assert.Equal("green", cmd.GetProperty("color").GetString());
        Assert.Equal("bank", cmd.GetProperty("deny")[0].GetString());
        Assert.Equal("Looking", cmd.GetProperty("say").GetString());
        Assert.Equal(1, cmd.GetProperty("args").GetProperty("x").GetInt32());
        await chrome.SendAsync(new { type = "result", id = cmd.GetProperty("id").GetString(), ok = true, result = new { text = "2 tabs", image = new { data = "AAAA", mimeType = "image/jpeg" } } });
        Assert.Equal(new BrowserReply(true, "2 tabs", "AAAA", "image/jpeg"), await pending.WaitAsync(T));

        var failing = rig.Server.SendAsync(Cmd("click"), T, default);
        var click = await chrome.CommandAsync("click");
        await chrome.SendAsync(new { type = "result", id = click.GetProperty("id").GetString(), ok = false, error = "Snapshot again." });
        Assert.Equal(new BrowserReply(false, "Snapshot again."), await failing.WaitAsync(T));
    }

    [Fact]
    public async Task SecondProfileIsNotSelected()
    {
        var pipe = NewPipe();
        await using var rig = new ServerRig(pipe);
        await using var a = new FakeChrome(pipe);
        await a.HelloAsync("profile-a");
        Assert.True((await a.ReadyAsync()).GetProperty("selected").GetBoolean());
        await using var b = new FakeChrome(pipe);
        await b.HelloAsync("profile-b");
        var readyB = await b.ReadyAsync();
        Assert.False(readyB.GetProperty("selected").GetBoolean());
        Assert.True(readyB.GetProperty("app").GetBoolean());
        Assert.Equal("profile-a", rig.Stored);

        rig.Server.Select("profile-b");
        Assert.False((await a.ReadyAsync()).GetProperty("selected").GetBoolean());
        Assert.True((await b.ReadyAsync()).GetProperty("selected").GetBoolean());
        Assert.Equal("profile-b", rig.Stored);
        Assert.Equal(["profile-a", "profile-b"], rig.Server.Instances.Select(i => i.Id));
        Assert.Equal("profile-b", Assert.Single(rig.Server.Instances, i => i.Selected).Id);

        // The popup's select carries no id: it means "this profile".
        await a.SendAsync(new { type = "select" });
        Assert.True((await a.ReadyAsync()).GetProperty("selected").GetBoolean());
        Assert.False((await b.ReadyAsync()).GetProperty("selected").GetBoolean());
        Assert.Equal("profile-a", rig.Stored);
    }

    [Fact]
    public async Task StoredProfileWinsTheSelectionBack()
    {
        var pipe = NewPipe();
        await using var rig = new ServerRig(pipe, stored: "profile-a");
        await using var b = new FakeChrome(pipe);
        await b.HelloAsync("profile-b");
        Assert.True((await b.ReadyAsync()).GetProperty("selected").GetBoolean());
        Assert.Equal("profile-a", rig.Stored); // a fallback is never saved over a stored choice

        await using var a = new FakeChrome(pipe);
        await a.HelloAsync("profile-a");
        Assert.True((await a.ReadyAsync()).GetProperty("selected").GetBoolean());
        Assert.False((await b.ReadyAsync()).GetProperty("selected").GetBoolean());
        Assert.Equal("profile-a", Assert.Single(rig.Server.Instances, i => i.Selected).Id);
        Assert.Equal("profile-a", rig.Stored);
    }

    [Fact]
    public async Task PreCancelledCallerLeavesOtherCommandsRunning()
    {
        var pipe = NewPipe();
        await using var rig = new ServerRig(pipe);
        await using var chrome = new FakeChrome(pipe);
        await chrome.HelloAsync("profile-a");
        await chrome.ReadyAsync();

        var inFlight = rig.Server.SendAsync(Cmd("snapshot"), T, default);
        var cmd = await chrome.CommandAsync("snapshot");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Server.SendAsync(Cmd("extract", "task-2"), T, cancelled.Token));
        await rig.Server.EndTaskAsync("task-2", true, cancelled.Token).WaitAsync(T);

        await chrome.SendAsync(new { type = "result", id = cmd.GetProperty("id").GetString(), ok = true, result = new { text = "still here" } });
        Assert.Equal(new BrowserReply(true, "still here"), await inFlight.WaitAsync(T));
        Assert.True(rig.Server.IsConnected);
    }

    [Fact]
    public async Task HostWithoutAppReportsAppFalseThenConnects()
    {
        var pipe = NewPipe();
        await using var chrome = new FakeChrome(pipe);
        await chrome.HelloAsync("profile-a");
        var down = await chrome.ReadyAsync();
        Assert.False(down.GetProperty("app").GetBoolean());
        Assert.False(down.GetProperty("selected").GetBoolean());

        await using var rig = new ServerRig(pipe);
        var up = await chrome.ReadyAsync();
        Assert.True(up.GetProperty("app").GetBoolean());
        Assert.True(up.GetProperty("selected").GetBoolean());
        Assert.True(rig.Server.IsConnected);
    }

    [Fact]
    public async Task OpenAppLaunchesOnlyWhilePipeIsDown()
    {
        var pipe = NewPipe();
        await using var chrome = new FakeChrome(pipe);
        await chrome.HelloAsync("profile-a");
        await chrome.SendAsync(new { type = "openApp" });
        Assert.False((await chrome.ReadyAsync()).GetProperty("app").GetBoolean());
        await WaitUntil(() => chrome.Launches == 1);

        await using var rig = new ServerRig(pipe);
        Assert.True((await chrome.ReadyAsync()).GetProperty("app").GetBoolean());
        await chrome.SendAsync(new { type = "openApp" });
        await chrome.HelloAsync("profile-a"); // stdin is handled in order: this ready proves openApp was seen
        Assert.True((await chrome.ReadyAsync()).GetProperty("app").GetBoolean());
        Assert.Equal(1, chrome.Launches);
    }

    [Fact]
    public async Task TimeoutFailsOnlyThatCommand()
    {
        var pipe = NewPipe();
        await using var rig = new ServerRig(pipe);
        await using var chrome = new FakeChrome(pipe);
        await chrome.HelloAsync("profile-a");
        await chrome.ReadyAsync();

        var slow = rig.Server.SendAsync(Cmd("snapshot"), TimeSpan.FromMilliseconds(300), default);
        var slowCmd = await chrome.CommandAsync("snapshot");
        var fast = rig.Server.SendAsync(Cmd("extract"), T, default);
        var fastCmd = await chrome.CommandAsync("extract");
        Assert.Equal(new BrowserReply(false, "The browser didn't answer in time."), await slow.WaitAsync(T));

        await chrome.SendAsync(new { type = "result", id = fastCmd.GetProperty("id").GetString(), ok = true, result = new { text = "fast" } });
        await chrome.SendAsync(new { type = "result", id = slowCmd.GetProperty("id").GetString(), ok = true, result = new { text = "late" } });
        Assert.Equal(new BrowserReply(true, "fast"), await fast.WaitAsync(T));

        var next = rig.Server.SendAsync(Cmd("scroll"), T, default);
        var nextCmd = await chrome.CommandAsync("scroll");
        await chrome.SendAsync(new { type = "result", id = nextCmd.GetProperty("id").GetString(), ok = true, result = new { text = "next" } });
        Assert.Equal(new BrowserReply(true, "next"), await next.WaitAsync(T));
        Assert.True(rig.Server.IsConnected);
    }

    [Fact]
    public async Task DisconnectFailsPendingCommands()
    {
        var pipe = NewPipe();
        await using var rig = new ServerRig(pipe);
        await using var chrome = new FakeChrome(pipe);
        await chrome.HelloAsync("profile-a");
        await chrome.ReadyAsync();

        var first = rig.Server.SendAsync(Cmd("snapshot"), T, default);
        var second = rig.Server.SendAsync(Cmd("extract", "task-2"), T, default);
        await chrome.CommandAsync("snapshot");
        await chrome.CommandAsync("extract");
        chrome.Stdin.Complete(); // Chrome closed the port: the host exits and drops the pipe

        var gone = new BrowserReply(false, "The browser disconnected.");
        Assert.Equal(gone, await first.WaitAsync(T));
        Assert.Equal(gone, await second.WaitAsync(T));
        await WaitUntil(() => !rig.Server.IsConnected && rig.Server.Instances.Count == 0);
        Assert.False((await rig.Server.SendAsync(Cmd("tabs"), T, default)).Ok);
    }

    [Fact]
    public async Task WrongProofIsDropped()
    {
        var pipe = NewPipe();
        await using var rig = new ServerRig(pipe);
        await using var raw = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
        await raw.ConnectAsync(5000);
        using (var challenge = await ChromeBridgeNativeProtocol.ReadAsync(raw, default).WaitAsync(T))
            Assert.Equal("challenge", challenge!.RootElement.GetProperty("type").GetString());
        await ChromeBridgeNativeProtocol.WriteAsync(raw, new { type = "proof", value = Convert.ToBase64String(new byte[32]) }, default);
        try { await ChromeBridgeNativeProtocol.WriteAsync(raw, new { type = "hello", protocol = 2, profileId = "intruder", brand = "Chrome" }, default); }
        catch (IOException) { }

        var after = await Record.ExceptionAsync(async () => Assert.Null(await ChromeBridgeNativeProtocol.ReadAsync(raw, default).WaitAsync(T)));
        Assert.True(after is null or IOException, after?.ToString());
        Assert.Empty(rig.Server.Instances);
        Assert.False(rig.Server.IsConnected);
    }

    [Fact]
    public async Task StopMessageRaisesStopRequested()
    {
        var pipe = NewPipe();
        await using var rig = new ServerRig(pipe);
        var stops = Channel.CreateUnbounded<string?>();
        rig.Server.StopRequested += task => stops.Writer.TryWrite(task);
        await using var chrome = new FakeChrome(pipe);
        await chrome.HelloAsync("profile-a");
        await chrome.ReadyAsync();

        await chrome.SendAsync(new { type = "stop", task = "task-9" });
        Assert.Equal("task-9", await stops.Reader.ReadAsync().AsTask().WaitAsync(T));
        await chrome.SendAsync(new { type = "stop" });
        Assert.Null(await stops.Reader.ReadAsync().AsTask().WaitAsync(T));
    }

    [Fact]
    public async Task HelloSendsSweepWithTrackedTasks()
    {
        var pipe = NewPipe();
        await using var rig = new ServerRig(pipe);
        var tracked = rig.Server.TrackTask("task-7");
        Assert.False(rig.Server.IsWorking);

        await using var chrome = new FakeChrome(pipe);
        await chrome.HelloAsync("profile-a");
        await chrome.ReadyAsync();
        var sweep = await chrome.CommandAsync("sweep");
        Assert.Equal("", sweep.GetProperty("task").GetString());
        Assert.Equal("blue", sweep.GetProperty("color").GetString());
        Assert.Equal(0, sweep.GetProperty("deny").GetArrayLength());
        Assert.Equal(["task-7"], sweep.GetProperty("args").GetProperty("running").EnumerateArray().Select(e => e.GetString()));
        Assert.True(rig.Server.IsWorking);
        tracked.Dispose();
        Assert.False(rig.Server.IsWorking);

        var end = rig.Server.EndTaskAsync("task-7", true, default);
        var endCmd = await chrome.CommandAsync("end");
        Assert.Equal("task-7", endCmd.GetProperty("task").GetString());
        Assert.True(endCmd.GetProperty("args").GetProperty("tidy").GetBoolean());
        await chrome.SendAsync(new { type = "result", id = endCmd.GetProperty("id").GetString(), ok = true, result = new { text = "Ended." } });
        await end.WaitAsync(T);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + T;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Condition not met.");
            await Task.Delay(20);
        }
    }

    private sealed class ServerRig : IAsyncDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _run;
        public string? Stored;
        public BrowserBridgeServer Server { get; }

        public ServerRig(string pipe, string? stored = null)
        {
            Stored = stored;
            Server = new BrowserBridgeServer(Secret, pipe, () => Stored, id => Stored = id);
            _run = Server.RunAsync(_cts.Token);
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            await Server.DisposeAsync();
            await _run.WaitAsync(T);
        }
    }

    private sealed class FakeChrome : IAsyncDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _run;
        private int _launches;
        public ChannelStream Stdin { get; } = new();
        public ChannelStream Stdout { get; } = new();
        public int Launches => Volatile.Read(ref _launches);

        public FakeChrome(string pipe) =>
            _run = BrowserNativeHost.RunAsync(Stdin, Stdout, () => Secret, pipe, () => Interlocked.Increment(ref _launches), _cts.Token);

        public Task SendAsync(object message) => ChromeBridgeNativeProtocol.WriteAsync(Stdin, message, default);
        public Task HelloAsync(string profile) => SendAsync(new { type = "hello", protocol = 2, profileId = profile, brand = "Chrome" });
        public Task<JsonElement> ReadyAsync() => NextAsync(m => Type(m) == "ready");
        public Task<JsonElement> CommandAsync(string op) => NextAsync(m => Type(m) == "command" && m.GetProperty("op").GetString() == op);

        private async Task<JsonElement> NextAsync(Func<JsonElement, bool> match)
        {
            while (true)
            {
                using var doc = await ChromeBridgeNativeProtocol.ReadAsync(Stdout, default).WaitAsync(T)
                    ?? throw new EndOfStreamException("The host closed stdout.");
                if (match(doc.RootElement)) return doc.RootElement.Clone();
            }
        }

        private static string? Type(JsonElement m) => m.TryGetProperty("type", out var t) ? t.GetString() : null;

        public async ValueTask DisposeAsync()
        {
            Stdin.Complete();
            try { await _run.WaitAsync(T); }
            finally { _cts.Cancel(); }
        }
    }

    /// <summary>One-way in-memory byte stream standing in for Chrome's stdin or stdout.</summary>
    private sealed class ChannelStream : Stream
    {
        private readonly Channel<byte[]> _chunks = Channel.CreateUnbounded<byte[]>();
        private byte[] _current = [];
        private int _offset;

        public void Complete() => _chunks.Writer.TryComplete();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            while (_offset == _current.Length)
            {
                if (!await _chunks.Reader.WaitToReadAsync(token).ConfigureAwait(false)) return 0;
                if (_chunks.Reader.TryRead(out var next)) { _current = next; _offset = 0; }
            }
            var count = Math.Min(buffer.Length, _current.Length - _offset);
            _current.AsMemory(_offset, count).CopyTo(buffer);
            _offset += count;
            return count;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default)
        {
            if (!_chunks.Writer.TryWrite(buffer.ToArray())) throw new IOException("The stream is closed.");
            return ValueTask.CompletedTask;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) => ReadAsync(buffer.AsMemory(offset, count), token).AsTask();
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken token) => WriteAsync(buffer.AsMemory(offset, count), token).AsTask();
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count, default).GetAwaiter().GetResult();
        public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer, offset, count, default).GetAwaiter().GetResult();
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken token) => Task.CompletedTask;
        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
