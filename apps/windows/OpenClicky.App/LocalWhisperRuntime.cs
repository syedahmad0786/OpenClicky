using System.Diagnostics;
using OpenClicky.Core.Dictation;
using Whisper.net;

namespace OpenClicky.App;

internal interface ILocalWhisperEngine : IDisposable
{
    Task<string> TranscribeAsync(float[] samples, WhisperDecodeOptions options, CancellationToken token);
}

/// <summary>
/// Owns one verified native Whisper model across voice turns and serializes model inference. Asking for another model
/// disposes the loaded one first, so at most one model is in memory.
/// </summary>
internal sealed class LocalWhisperRuntime : IDisposable
{
    private readonly SemaphoreSlim _inference = new(1, 1);
    private readonly Func<WhisperModelInfo, CancellationToken, Task> _verify;
    private readonly Func<WhisperModelInfo, ILocalWhisperEngine> _load;
    private readonly Action<string>? _log;
    private ILocalWhisperEngine? _engine;
    private WhisperModelInfo? _model;
    private int _disposeState;

    internal LocalWhisperRuntime(Action<string>? log = null) : this(
        (model, token) => LocalWhisperModel.VerifyAsync(model, LocalWhisperModel.PathOnDisk(model), token),
        model => new WhisperEngine(WhisperFactory.FromPath(LocalWhisperModel.PathOnDisk(model))), log) { }

    internal LocalWhisperRuntime(Func<WhisperModelInfo, CancellationToken, Task> verify, Func<WhisperModelInfo, ILocalWhisperEngine> load, Action<string>? log = null)
    {
        _verify = verify;
        _load = load;
        _log = log;
    }

    /// <summary>Whether <paramref name="model"/> is the loaded one (a hint for warming; the next call checks again).</summary>
    internal bool IsWarmFor(WhisperModelInfo model) => Volatile.Read(ref _model) == model && Volatile.Read(ref _engine) is not null;
    internal bool IsDisposed => Volatile.Read(ref _disposeState) != 0;

    internal async Task WarmAsync(WhisperModelInfo model, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        await _inference.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            await LoadAsync(model, token).ConfigureAwait(false);
        }
        finally { _inference.Release(); }
    }

    internal async Task<string> TranscribeAsync(WhisperModelInfo model, float[] samples, WhisperDecodeOptions options, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        await _inference.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            var engine = await LoadAsync(model, token).ConfigureAwait(false);
            return await engine.TranscribeAsync(samples, options, token).ConfigureAwait(false);
        }
        finally { _inference.Release(); }
    }

    /// <summary>Runs under <see cref="_inference"/>. Verifies the new model before dropping the loaded one.</summary>
    private async Task<ILocalWhisperEngine> LoadAsync(WhisperModelInfo model, CancellationToken token)
    {
        if (_engine is not null && _model == model) return _engine;
        var clock = Stopwatch.StartNew();
        await _verify(model, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        Volatile.Write(ref _model, null);
        _engine?.Dispose();
        _engine = null;
        _engine = _load(model);
        Volatile.Write(ref _model, model);
        _log?.Invoke($"local_whisper_model_load_ms={clock.ElapsedMilliseconds} model={model.Id}");
        return _engine;
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
        public async Task<string> TranscribeAsync(float[] samples, WhisperDecodeOptions options, CancellationToken token)
        {
            // One thread per physical core (logical / 2 on SMT CPUs), 4 to 8: small.en is ~3x the work of base.en.
            var threads = Math.Clamp(Environment.ProcessorCount / 2, Math.Min(4, Environment.ProcessorCount), 8);
            var builder = factory.CreateBuilder().WithThreads(threads).WithNoContext().WithTemperature(0);
            // Whisper.net's own detection option (an empty language, which whisper.cpp treats like "auto").
            builder = options.Language == "auto" ? builder.WithLanguageDetection() : builder.WithLanguage(options.Language);
            if (options.Prompt is not null) builder = builder.WithPrompt(options.Prompt);
            await using var processor = builder.Build();
            var words = new List<string>();
            await foreach (var segment in processor.ProcessAsync(samples, token).ConfigureAwait(false)) words.Add(segment.Text.Trim());
            return string.Join(" ", words).Trim();
        }

        public void Dispose() => factory.Dispose();
    }
}
