using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;

namespace OpenClicky.Providers;

public sealed class ChromeBridgePipeTransport : IAsyncDisposable
{
    public const string PipeName = "OpenClicky.ChromeBridge.v1";
    private readonly byte[] _secret;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private NamedPipeServerStream? _pipe;
    private TaskCompletionSource _connectionEnded = CompletedSignal();

    public ChromeBridgePipeTransport(byte[] secret) => _secret = secret.Length == 32 ? secret.ToArray() : throw new ArgumentException("Chrome bridge secret must be 32 bytes.", nameof(secret));
    public bool IsConnected => _pipe is { IsConnected: true };
    public Task ConnectionEnded => _connectionEnded.Task;

    public async Task WaitForNativeHostAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await DisposePipeAsync().ConfigureAwait(false);
            _pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await _pipe.WaitForConnectionAsync(token).ConfigureAwait(false);
            var challenge = RandomNumberGenerator.GetBytes(32);
            await ChromeBridgeNativeProtocol.WriteAsync(_pipe, new { type = "challenge", value = Convert.ToBase64String(challenge) }, token).ConfigureAwait(false);
            using var response = await ChromeBridgeNativeProtocol.ReadAsync(_pipe, token).ConfigureAwait(false)
                ?? throw new IOException("Native host disconnected during authentication.");
            var expected = HMACSHA256.HashData(_secret, challenge);
            if (!response.RootElement.TryGetProperty("type", out var type) || type.GetString() != "proof"
                || !response.RootElement.TryGetProperty("value", out var proof)
                || !TryDecode(proof.GetString(), out var supplied) || !CryptographicOperations.FixedTimeEquals(supplied, expected))
                throw new UnauthorizedAccessException("Chrome native-host authentication failed.");
            _connectionEnded = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        catch { await DisposePipeAsync().ConfigureAwait(false); throw; }
        finally { _gate.Release(); }
    }

    public async Task<JsonDocument> RequestAsync(object request, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_pipe is not { IsConnected: true }) throw new InvalidOperationException("Chrome native host is not connected.");
            try
            {
                await ChromeBridgeNativeProtocol.WriteAsync(_pipe, request, token).ConfigureAwait(false);
                return await ChromeBridgeNativeProtocol.ReadAsync(_pipe, token).ConfigureAwait(false)
                    ?? throw new IOException("Chrome native host disconnected before responding.");
            }
            catch { await DisposePipeAsync().ConfigureAwait(false); throw; }
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync() { await DisposePipeAsync().ConfigureAwait(false); _gate.Dispose(); }
    private async Task DisposePipeAsync()
    {
        if (_pipe is not null) { await _pipe.DisposeAsync().ConfigureAwait(false); _pipe = null; }
        _connectionEnded.TrySetResult();
    }
    private static TaskCompletionSource CompletedSignal()
    { var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); signal.SetResult(); return signal; }
    private static bool TryDecode(string? value, out byte[] bytes)
    {
        bytes = new byte[32];
        return value is not null && Convert.TryFromBase64String(value, bytes, out var written) && written == bytes.Length;
    }
}

public static class ChromeBridgeNativeRelay
{
    public static async Task RunAsync(Stream chromeInput, Stream chromeOutput, byte[] secret, CancellationToken token)
    {
        await using var pipe = new NamedPipeClientStream(".", ChromeBridgePipeTransport.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(10_000, token).ConfigureAwait(false);
        using var challengeMessage = await ChromeBridgeNativeProtocol.ReadAsync(pipe, token).ConfigureAwait(false)
            ?? throw new IOException("OpenClicky disconnected during authentication.");
        if (challengeMessage.RootElement.GetProperty("type").GetString() != "challenge") throw new UnauthorizedAccessException("Invalid Chrome bridge challenge.");
        var challenge = Convert.FromBase64String(challengeMessage.RootElement.GetProperty("value").GetString() ?? "");
        if (challenge.Length != 32) throw new UnauthorizedAccessException("Invalid Chrome bridge challenge.");
        var proof = Convert.ToBase64String(HMACSHA256.HashData(secret, challenge));
        await ChromeBridgeNativeProtocol.WriteAsync(pipe, new { type = "proof", value = proof }, token).ConfigureAwait(false);
        while (!token.IsCancellationRequested)
        {
            using var request = await ChromeBridgeNativeProtocol.ReadAsync(pipe, token).ConfigureAwait(false);
            if (request is null) return;
            await ChromeBridgeNativeProtocol.WriteAsync(chromeOutput, request.RootElement, token).ConfigureAwait(false);
            using var response = await ChromeBridgeNativeProtocol.ReadAsync(chromeInput, token).ConfigureAwait(false);
            if (response is null) return;
            await ChromeBridgeNativeProtocol.WriteAsync(pipe, response.RootElement, token).ConfigureAwait(false);
        }
    }
}
