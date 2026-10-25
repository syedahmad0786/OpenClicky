using System.IO.Pipes;
using System.Text.Json;

namespace OpenClicky.Providers;

/// <summary>
/// The process Chrome starts for native messaging. It relays Chrome's stdin/stdout to the app's pipe in both directions,
/// survives the app restarting, and opens the app on the popup's request when the pipe is down.
/// </summary>
public static class BrowserNativeHost
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
    private const int ConnectTimeoutMs = 1500;
    private static readonly TimeSpan AuthTimeout = TimeSpan.FromSeconds(5);

    public static async Task RunAsync(Stream chromeInput, Stream chromeOutput, Func<byte[]> secret, string pipeName, Action launchApp, CancellationToken token)
    {
        using var life = CancellationTokenSource.CreateLinkedTokenSource(token);
        var relay = new Relay(chromeOutput);
        var pipeLoop = PipeLoopAsync(relay, secret, pipeName, life.Token);
        try
        {
            while (true)
            {
                using var message = await ChromeBridgeNativeProtocol.ReadAsync(chromeInput, life.Token, ChromeBridgeNativeProtocol.MaxInboundBytes).ConfigureAwait(false);
                if (message is null) return; // Chrome closed the port: the process ends
                var root = message.RootElement;
                var type = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                NamedPipeClientStream? pipe;
                lock (relay)
                {
                    if (type == "hello") relay.LastHello = root.Clone();
                    pipe = relay.Pipe;
                }
                if (type == "openApp")
                {
                    if (pipe is null) { try { launchApp(); } catch { /* the popup shows offline either way */ } }
                    continue;
                }
                if (pipe is not null) await relay.ToPipeAsync(pipe, root, life.Token).ConfigureAwait(false); // else dropped
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            life.Cancel();
            try { await pipeLoop.ConfigureAwait(false); } catch { /* shutting down */ }
        }
    }

    private static async Task PipeLoopAsync(Relay relay, Func<byte[]> secret, string pipeName, CancellationToken token)
    {
        var announcedDown = false;
        while (!token.IsCancellationRequested)
        {
            var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await pipe.ConnectAsync(ConnectTimeoutMs, token).ConfigureAwait(false);
                using (var auth = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    auth.CancelAfter(AuthTimeout);
                    await BrowserPipeAuth.AnswerServerAsync(pipe, secret(), auth.Token).ConfigureAwait(false);
                }
                JsonElement? hello;
                lock (relay) { relay.Pipe = pipe; hello = relay.LastHello; }
                announcedDown = false;
                if (hello is { } h) await relay.ToPipeAsync(pipe, h, token).ConfigureAwait(false);
                while (true)
                {
                    using var message = await ChromeBridgeNativeProtocol.ReadAsync(pipe, token).ConfigureAwait(false);
                    if (message is null) break;
                    await relay.ToChromeAsync(message.RootElement, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch { /* app not running, restarting, or failed auth: retry */ }
            finally
            {
                lock (relay) { if (relay.Pipe == pipe) relay.Pipe = null; }
                await pipe.DisposeAsync().ConfigureAwait(false);
            }
            if (!announcedDown)
            {
                announcedDown = true;
                try { await relay.ToChromeAsync(new { type = "ready", selected = false, app = false }, token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                catch { /* Chrome is gone; stdin ends shortly */ }
            }
            try { await Task.Delay(RetryDelay, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    private sealed class Relay(Stream chromeOutput)
    {
        private readonly SemaphoreSlim _stdout = new(1, 1);
        private readonly SemaphoreSlim _pipe = new(1, 1);
        public NamedPipeClientStream? Pipe;
        public JsonElement? LastHello;

        public async Task ToChromeAsync(object message, CancellationToken token)
        {
            await _stdout.WaitAsync(token).ConfigureAwait(false);
            try { await ChromeBridgeNativeProtocol.WriteAsync(chromeOutput, message, token).ConfigureAwait(false); }
            finally { _stdout.Release(); }
        }

        /// <summary>A failed write is dropped; the pipe loop sees the broken pipe and reconnects.</summary>
        public async Task ToPipeAsync(NamedPipeClientStream pipe, JsonElement message, CancellationToken token)
        {
            await _pipe.WaitAsync(token).ConfigureAwait(false);
            try { await ChromeBridgeNativeProtocol.WriteAsync(pipe, message, token, ChromeBridgeNativeProtocol.MaxInboundBytes).ConfigureAwait(false); }
            catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException or InvalidDataException) { }
            finally { _pipe.Release(); }
        }
    }
}
