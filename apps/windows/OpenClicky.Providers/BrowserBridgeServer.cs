using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using OpenClicky.Core;

namespace OpenClicky.Providers;

public sealed record BrowserInstance(string Id, string Brand, DateTimeOffset ConnectedAt, bool Selected);

/// <summary>
/// App side of the browser bridge: accepts up to four authenticated native-host connections (one per browser profile),
/// keeps exactly one selected and sends it commands. The pipe stays on this PC and is limited to the current user.
/// </summary>
public sealed class BrowserBridgeServer : IBrowserBridge, IAsyncDisposable
{
    public const string DefaultPipeName = "OpenClicky.ChromeBridge.v2";
    public static string PipeName => Environment.GetEnvironmentVariable("OPENCLICKY_BROWSER_PIPE") is { Length: > 0 } n ? n : DefaultPipeName;

    private const int MaxConnections = 4;
    private const string NotConnected = "The browser isn't connected. Connect it in Settings → Integrations.";
    private const string NoAnswer = "The browser didn't answer in time.";
    private const string Disconnected = "The browser disconnected.";
    private static readonly TimeSpan AuthTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(5);
    private static readonly JsonElement EmptyArgs = JsonDocument.Parse("{}").RootElement.Clone();

    private readonly byte[] _secret;
    private readonly string _pipeName;
    private readonly Func<string?> _storedSelection;
    private readonly Action<string> _saveSelection;
    private readonly CancellationTokenSource _life = new();
    private readonly SemaphoreSlim _slots = new(MaxConnections, MaxConnections);
    private readonly SemaphoreSlim _announceGate = new(1, 1);
    private readonly object _sync = new();
    private readonly List<Connection> _connections = [];
    private readonly Dictionary<string, int> _running = new(StringComparer.Ordinal);
    private string? _selectedId; // preferred profile (stored); may be offline
    private string? _effective;  // the connected, selected profile
    private long _nextId;
    private long _helloOrder;

    public BrowserBridgeServer(byte[] secret, string pipeName, Func<string?> storedSelection, Action<string> saveSelection)
    {
        _secret = secret.Length == 32 ? secret.ToArray() : throw new ArgumentException("Browser bridge secret must be 32 bytes.", nameof(secret));
        _pipeName = pipeName;
        _storedSelection = storedSelection;
        _saveSelection = saveSelection;
    }

    public event Action? Changed;
    public event Action<string?>? StopRequested;

    public bool IsConnected { get { lock (_sync) return _effective is not null; } }
    public bool IsWorking { get { lock (_sync) return _effective is not null && _running.Count > 0; } }

    public IReadOnlyList<BrowserInstance> Instances
    {
        get
        {
            lock (_sync)
                return Named().Select(c => new BrowserInstance(c.ProfileId!, c.Brand, c.ConnectedAt, c.ProfileId == _effective)).ToList();
        }
    }

    public async Task RunAsync(CancellationToken token)
    {
        using var run = CancellationTokenSource.CreateLinkedTokenSource(token, _life.Token);
        var handlers = new List<Task>();
        try
        {
            while (true)
            {
                await _slots.WaitAsync(run.Token).ConfigureAwait(false);
                NamedPipeServerStream pipe;
                try
                {
                    pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, MaxConnections, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                }
                catch { _slots.Release(); throw; }
                try { await pipe.WaitForConnectionAsync(run.Token).ConfigureAwait(false); }
                catch { await pipe.DisposeAsync().ConfigureAwait(false); _slots.Release(); throw; }
                handlers.RemoveAll(h => h.IsCompleted);
                handlers.Add(HandleAsync(pipe, run.Token));
            }
        }
        catch (OperationCanceledException) when (run.IsCancellationRequested) { }
        finally
        {
            Connection[] open;
            lock (_sync) open = _connections.ToArray();
            foreach (var connection in open) connection.Pipe.Dispose();
            await Task.WhenAll(handlers).ConfigureAwait(false);
        }
    }

    public async Task<BrowserReply> SendAsync(BrowserCommand command, TimeSpan timeout, CancellationToken token)
    {
        Connection? connection;
        lock (_sync) connection = Selected();
        if (connection is null) return new BrowserReply(false, NotConnected);

        var id = "c" + Interlocked.Increment(ref _nextId);
        var pending = new TaskCompletionSource<BrowserReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Pending[id] = pending;
        try
        {
            if (connection.Closed) return new BrowserReply(false, Disconnected);
            using var timer = CancellationTokenSource.CreateLinkedTokenSource(token);
            timer.CancelAfter(timeout);
            var written = false;
            try
            {
                await connection.WriteAsync(new
                {
                    type = "command", id, op = command.Op, task = command.Task, owner = command.Owner, groupTitle = command.GroupTitle,
                    color = command.Color, deny = command.Deny, say = command.Say,
                    args = command.Args.ValueKind == JsonValueKind.Undefined ? EmptyArgs : command.Args,
                }, timer.Token).ConfigureAwait(false);
                written = true;
                return await pending.Task.WaitAsync(timer.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (!written) connection.Pipe.Dispose(); // a browser that stopped reading, or a half-written frame
                if (token.IsCancellationRequested) throw;
                return new BrowserReply(false, NoAnswer);
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
            {
                connection.Pipe.Dispose(); // its read loop cleans up
                return new BrowserReply(false, Disconnected);
            }
        }
        finally { connection.Pending.TryRemove(id, out _); }
    }

    public IDisposable TrackTask(string task)
    {
        lock (_sync) _running[task] = _running.GetValueOrDefault(task) + 1;
        Changed?.Invoke();
        return new Tracker(this, task);
    }

    public void Select(string instanceId)
    {
        lock (_sync)
        {
            if (!Named().Any(c => c.ProfileId == instanceId) || _effective == instanceId) return;
            _selectedId = instanceId;
        }
        _ = AnnounceAsync(null);
    }

    /// <summary>Ends a task's browser work (tidy closes its tabs). Waits up to 10 s; never throws.</summary>
    public async Task EndTaskAsync(string task, bool tidy, CancellationToken token)
    {
        try
        {
            await SendAsync(new BrowserCommand("end", task, "", "", "blue", [], null, JsonSerializer.SerializeToElement(new { tidy })),
                TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
        }
        catch { /* best effort: the next sweep releases it */ }
    }

    public async ValueTask DisposeAsync()
    {
        _life.Cancel();
        Connection[] open;
        lock (_sync) open = _connections.ToArray();
        foreach (var connection in open) await CloseAsync(connection).ConfigureAwait(false);
    }

    private async Task HandleAsync(NamedPipeServerStream pipe, CancellationToken token)
    {
        var connection = new Connection(pipe);
        try
        {
            using (var auth = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                auth.CancelAfter(AuthTimeout);
                if (!await BrowserPipeAuth.ProveClientAsync(pipe, _secret, auth.Token).ConfigureAwait(false)) return;
            }
            lock (_sync) _connections.Add(connection);
            while (true)
            {
                using var message = await ChromeBridgeNativeProtocol.ReadAsync(pipe, token, ChromeBridgeNativeProtocol.MaxInboundBytes).ConfigureAwait(false);
                if (message is null) return;
                OnMessage(connection, message.RootElement);
            }
        }
        catch { /* any failure ends this connection only */ }
        finally
        {
            await CloseAsync(connection).ConfigureAwait(false);
            _slots.Release();
        }
    }

    private void OnMessage(Connection connection, JsonElement message)
    {
        if (message.ValueKind != JsonValueKind.Object) return;
        switch (Str(message, "type"))
        {
            case "hello":
                OnHello(connection, message);
                break;
            case "result":
                if (Str(message, "id") is { } id && connection.Pending.TryRemove(id, out var pending)) pending.TrySetResult(ToReply(message));
                break;
            case "stop":
                try { StopRequested?.Invoke(Str(message, "task")); } catch { /* a faulty subscriber must not drop the browser */ }
                break;
            case "select":
                // The popup's select carries no id: it means "use this profile".
                if ((Str(message, "id") ?? connection.ProfileId) is { } chosen) Select(chosen);
                break;
        }
    }

    private void OnHello(Connection connection, JsonElement message)
    {
        if (Str(message, "profileId") is not { Length: > 0 and <= 128 } profile) return;
        Connection? stale;
        lock (_sync)
        {
            // The same profile on a second connection means the old one is dead (a restarted host).
            stale = _connections.FirstOrDefault(c => c != connection && c.ProfileId == profile);
            if (stale is not null) { _connections.Remove(stale); stale.ProfileId = null; }
            if (connection.ProfileId is null)
            {
                connection.ConnectedAt = DateTimeOffset.Now;
                connection.HelloOrder = ++_helloOrder;
            }
            connection.ProfileId = profile;
            connection.Brand = Str(message, "brand") is { Length: > 0 and <= 64 } brand ? brand : "Chrome";
        }
        stale?.Pipe.Dispose();
        _ = AnnounceAsync(connection);
    }

    /// <summary>Re-selects, saves a new selection, then sends ready (to everyone after a change, else to <paramref name="hello"/>) and a sweep to a selected hello.</summary>
    private async Task AnnounceAsync(Connection? hello)
    {
        await _announceGate.WaitAsync().ConfigureAwait(false);
        try { await AnnounceLockedAsync(hello).ConfigureAwait(false); }
        finally { _announceGate.Release(); }
        Changed?.Invoke();
    }

    private async Task AnnounceLockedAsync(Connection? hello)
    {
        string? effective, save = null;
        bool changed;
        Connection[] named;
        string[] running;
        lock (_sync)
        {
            _selectedId ??= _storedSelection();
            named = Named().ToArray();
            var pick = named.Any(c => c.ProfileId == _selectedId) ? _selectedId : named.FirstOrDefault()?.ProfileId;
            if (pick is not null && pick != _storedSelection()) save = pick;
            if (pick is not null) _selectedId = pick;
            changed = pick != _effective;
            effective = _effective = pick;
            running = _running.Keys.ToArray();
        }
        if (save is not null) { try { _saveSelection(save); } catch { /* settings write failed; selection still holds in memory */ } }
        var targets = changed ? named : hello is { ProfileId: not null } ? [hello] : [];
        foreach (var target in targets)
            await TryWriteAsync(target, new { type = "ready", selected = target.ProfileId == effective, app = true }).ConfigureAwait(false);
        if (hello is not null && hello.ProfileId == effective)
        {
            // Fire-and-forget: nothing waits for the sweep's result; it is dropped as unknown when it arrives.
            await TryWriteAsync(hello, new
            {
                type = "command", id = "c" + Interlocked.Increment(ref _nextId), op = "sweep", task = "", owner = "", groupTitle = "",
                color = "blue", deny = Array.Empty<string>(), args = new { running },
            }).ConfigureAwait(false);
        }
    }

    private async Task CloseAsync(Connection connection)
    {
        bool registered;
        lock (_sync) registered = _connections.Remove(connection) && connection.ProfileId is not null;
        connection.Closed = true;
        foreach (var id in connection.Pending.Keys)
            if (connection.Pending.TryRemove(id, out var pending)) pending.TrySetResult(new BrowserReply(false, Disconnected));
        await connection.Pipe.DisposeAsync().ConfigureAwait(false);
        if (registered && !_life.IsCancellationRequested) await AnnounceAsync(null).ConfigureAwait(false);
    }

    private static async Task TryWriteAsync(Connection connection, object message)
    {
        try
        {
            using var timeout = new CancellationTokenSource(WriteTimeout);
            await connection.WriteAsync(message, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { connection.Pipe.Dispose(); } // stopped reading, or a half-written frame
        catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException) { }
    }

    private void Untrack(string task)
    {
        lock (_sync)
        {
            if (!_running.TryGetValue(task, out var count)) return;
            if (count <= 1) _running.Remove(task); else _running[task] = count - 1;
        }
        Changed?.Invoke();
    }

    private IEnumerable<Connection> Named() => _connections.Where(c => c.ProfileId is not null).OrderBy(c => c.HelloOrder);
    private Connection? Selected() => _effective is null ? null : _connections.FirstOrDefault(c => c.ProfileId == _effective);

    private static BrowserReply ToReply(JsonElement message)
    {
        if (!(message.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True))
            return new BrowserReply(false, Str(message, "error") ?? "The browser reported an error.");
        if (!message.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object) return new BrowserReply(true, "");
        var text = Str(result, "text") ?? "";
        return result.TryGetProperty("image", out var image) && image.ValueKind == JsonValueKind.Object && Str(image, "data") is { } data
            ? new BrowserReply(true, text, data, Str(image, "mimeType"))
            : new BrowserReply(true, text);
    }

    private static string? Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private sealed class Connection(NamedPipeServerStream pipe)
    {
        private readonly SemaphoreSlim _writeGate = new(1, 1);
        public NamedPipeServerStream Pipe { get; } = pipe;
        public ConcurrentDictionary<string, TaskCompletionSource<BrowserReply>> Pending { get; } = new();
        public volatile bool Closed;
        public string? ProfileId;
        public string Brand = "Chrome";
        public DateTimeOffset ConnectedAt;
        public long HelloOrder;

        public async Task WriteAsync(object message, CancellationToken token)
        {
            await _writeGate.WaitAsync(token).ConfigureAwait(false);
            try { await ChromeBridgeNativeProtocol.WriteAsync(Pipe, message, token).ConfigureAwait(false); }
            finally { _writeGate.Release(); }
        }
    }

    private sealed class Tracker(BrowserBridgeServer owner, string task) : IDisposable
    {
        private int _disposed;
        public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) owner.Untrack(task); }
    }
}

/// <summary>HMAC-SHA256 challenge/proof on the first two pipe messages; the secret never crosses the pipe.</summary>
internal static class BrowserPipeAuth
{
    public static async Task<bool> ProveClientAsync(Stream pipe, byte[] secret, CancellationToken token)
    {
        var challenge = RandomNumberGenerator.GetBytes(32);
        await ChromeBridgeNativeProtocol.WriteAsync(pipe, new { type = "challenge", value = Convert.ToBase64String(challenge) }, token).ConfigureAwait(false);
        using var response = await ChromeBridgeNativeProtocol.ReadAsync(pipe, token).ConfigureAwait(false);
        if (response is null || response.RootElement.ValueKind != JsonValueKind.Object) return false;
        var root = response.RootElement;
        return root.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "proof"
            && root.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String
            && TryDecode(value.GetString(), out var supplied)
            && CryptographicOperations.FixedTimeEquals(supplied, HMACSHA256.HashData(secret, challenge));
    }

    public static async Task AnswerServerAsync(Stream pipe, byte[] secret, CancellationToken token)
    {
        using var message = await ChromeBridgeNativeProtocol.ReadAsync(pipe, token).ConfigureAwait(false)
            ?? throw new IOException("OpenClicky disconnected during authentication.");
        var root = message.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String
            || type.GetString() != "challenge" || !root.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.String
            || !TryDecode(value.GetString(), out var challenge))
            throw new UnauthorizedAccessException("Invalid browser bridge challenge.");
        await ChromeBridgeNativeProtocol.WriteAsync(pipe, new { type = "proof", value = Convert.ToBase64String(HMACSHA256.HashData(secret, challenge)) }, token).ConfigureAwait(false);
    }

    private static bool TryDecode(string? value, out byte[] bytes)
    {
        bytes = new byte[32];
        return value is not null && Convert.TryFromBase64String(value, bytes, out var written) && written == bytes.Length;
    }
}
