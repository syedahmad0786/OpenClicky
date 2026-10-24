using System.Diagnostics;
using Whisper.net;

namespace OpenClicky.App;

internal interface ILocalWhisperEngine : IDisposable
{
    Task<string> TranscribeAsync(float[] samples, CancellationToken token);
}

/// <summary>Owns the verified native Whisper model across voice turns and serializes model inference.</summary>
internal sealed class LocalWhisperRuntime : IDisposable
{
    private readonly SemaphoreSlim _inference = new(1, 1);
    private readonly Func<CancellationToken, Task> _verify;
    private readonly Func<ILocalWhisperEngine> _load;
    private readonly Action<string>? _log;
    private ILocalWhisperEngine? _engine;
    private int _disposeState;

    internal LocalWhisperRuntime(Action<string>? log = null) : this(
        token => LocalWhisperModel.VerifyAsync(LocalWhisperModel.PathOnDisk, token),
        () => new WhisperEngine(WhisperFactory.FromPath(LocalWhisperModel.PathOnDisk)), log) { }

    internal LocalWhisperRuntime(Func<CancellationToken, Task> verify, Func<ILocalWhisperEngine> load, Action<string>? log = null)
    {
        _verify = verify;
        _load = load;
        _log = log;
    }

    internal bool IsWarm => Volatile.Read(ref _engine) is not null;
    internal bool IsDisposed => Volatile.Read(ref _disposeState) != 0;

    internal async Task WarmAsync(CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        await _inference.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (_engine is not null) return;
            var clock = Stopwatch.StartNew();
            await _verify(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            _engine = _load();
            _log?.Invoke($"local_whisper_model_load_ms={clock.ElapsedMilliseconds}");
        }
        finally { _inference.Release(); }
    }

    internal async Task<string> TranscribeAsync(float[] samples, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        await _inference.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (_engine is null)
            {
                var clock = Stopwatch.StartNew();
                await _verify(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                _engine = _load();
                _log?.Invoke($"local_whisper_model_load_ms={clock.ElapsedMilliseconds}");
            }
            return await _engine.TranscribeAsync(samples, token).ConfigureAwait(false);
        }
        finally { _inference.Release(); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0) return;
        _inference.Wait();
        try { _engine?.Dispose(); _engine = null; }
        finally { _inference.Release(); _inference.Dispose(); }
    }

    private sealed class WhisperEngine(WhisperFactory factory) : ILocalWhisperEngine
    {
        public async Task<string> TranscribeAsync(float[] samples, CancellationToken token)
        {
            await using var processor = factory.CreateBuilder().WithLanguage("en").WithThreads(Math.Min(4, Environment.ProcessorCount))
                .WithNoContext().WithTemperature(0).Build();
            var words = new List<string>();
            await foreach (var segment in processor.ProcessAsync(samples, token).ConfigureAwait(false)) words.Add(segment.Text.Trim());
            return string.Join(" ", words).Trim();
        }

        public void Dispose() => factory.Dispose();
    }
}
