using NAudio.Wave;
using OpenClicky.Core;
using System.Diagnostics;

namespace OpenClicky.App;

internal sealed class LocalWhisperSttClient : ILiveSttClient
{
    private readonly IMicrophoneRecorder microphone;
    private readonly LocalWhisperRuntime runtime;
    private readonly Action<string>? log;
    private readonly bool ownsRuntime;
    private CancellationTokenSource? _listening;
    private CancellationTokenSource? _recognition;
    private readonly object _previewGate = new();
    private (byte[] Wav, Task<string> Text)? _preview;
    private Task? _previews;
    private Task? _endpointing;
    private Task? _warmup;
    private Stopwatch? _sessionClock;
    private bool _disposed;

    internal LocalWhisperSttClient(IMicrophoneRecorder microphone, Action<string>? log = null)
        : this(microphone, new LocalWhisperRuntime(log), log, ownsRuntime: true) { }

    internal LocalWhisperSttClient(IMicrophoneRecorder microphone, LocalWhisperRuntime runtime, Action<string>? log = null)
        : this(microphone, runtime, log, ownsRuntime: false) { }

    private LocalWhisperSttClient(IMicrophoneRecorder microphone, LocalWhisperRuntime runtime, Action<string>? log, bool ownsRuntime)
    {
        this.microphone = microphone;
        this.runtime = runtime;
        this.log = log;
        this.ownsRuntime = ownsRuntime;
    }
    public event Action<string>? TranscriptChanged;
    public event Action? SpeechStarted;
    public event Action? UtteranceEnded;
    public event Action<Exception>? ListeningFailed;
    public bool SupportsEchoCancelledInterruption => microphone.SupportsEchoCancellation;

    public void StartListening()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CancelListening();
        var session = _listening = new CancellationTokenSource();
        var recognition = _recognition = new CancellationTokenSource();
        lock (_previewGate) _preview = null;
        microphone.Start();
        _sessionClock = Stopwatch.StartNew();
        log?.Invoke("local_whisper_start");
        if (!runtime.IsWarm)
        {
            _warmup = Task.Run(async () =>
            {
                var clock = Stopwatch.StartNew();
                try
                {
                    await runtime.WarmAsync(session.Token);
                    log?.Invoke($"local_whisper_warmup_ms={clock.ElapsedMilliseconds}");
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { log?.Invoke($"local_whisper_warmup_error={ex.GetType().Name}"); }
            });
        }
        _endpointing = Task.Run(async () =>
        {
            var heardSpeech = false;
            var lastSpeech = DateTimeOffset.UtcNow;
            var firstSpeech = DateTimeOffset.MaxValue;
            try
            {
                while (true)
                {
                    await Task.Delay(200, session.Token);
                    if (!microphone.IsRecording) throw new InvalidOperationException("The selected microphone stopped or was disconnected");
                    var wav = microphone.SnapshotToWav();
                    if (VoiceActivity.HasRecentSpeech(wav))
                    {
                        if (!heardSpeech)
                        {
                            firstSpeech = DateTimeOffset.UtcNow;
                            SpeechStarted?.Invoke();
                        }
                        heardSpeech = true;
                        lastSpeech = DateTimeOffset.UtcNow;
                    }
                    else if (!heardSpeech && wav.Length > 16000 * 2 * 5 + 44)
                        microphone.DiscardBufferedAudio();
                    else if (heardSpeech && DateTimeOffset.UtcNow - lastSpeech >= TimeSpan.FromMilliseconds(900))
                    {
                        log?.Invoke($"local_whisper_endpoint_ms={_sessionClock?.ElapsedMilliseconds ?? 0}");
                        UtteranceEnded?.Invoke();
                        return;
                    }
                    if (heardSpeech && DateTimeOffset.UtcNow - firstSpeech >= TimeSpan.FromSeconds(55))
                        throw new InvalidOperationException("Keep each voice request shorter than one minute");
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!session.IsCancellationRequested) ListeningFailed?.Invoke(ex); }
        });
        _previews = Task.Run(async () =>
        {
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
                while (true)
                {
                    await timer.WaitForNextTickAsync(session.Token);
                    var wav = microphone.SnapshotToWav();
                    Task<string> pending;
                    lock (_previewGate)
                    {
                        session.Token.ThrowIfCancellationRequested();
                        if (_preview is { } previous && previous.Text.IsCompletedSuccessfully && CanReusePreview(previous.Wav, wav)) continue;
                        pending = TranscribeAsync(wav, recognition.Token);
                        _preview = (wav, pending);
                    }
                    var text = await pending;
                    if (!session.IsCancellationRequested) TranscriptChanged?.Invoke(text);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception) { /* Final recognition reports the error; a preview must not break recording. */ }
        });
    }

    public async Task<string> FinishListeningAsync(CancellationToken cancellationToken)
    {
        try
        {
            var finishClock = Stopwatch.StartNew();
            _listening?.Cancel();
            var wav = microphone.StopToWav();
            (byte[] Wav, Task<string> Text)? preview;
            lock (_previewGate) preview = _preview;
            var reuse = preview is { } candidate && !candidate.Text.IsCanceled && !candidate.Text.IsFaulted && CanReusePreview(candidate.Wav, wav);
            if (!reuse) _recognition?.Cancel();
            if (_previews is not null) await _previews.WaitAsync(cancellationToken);
            if (_endpointing is not null) await _endpointing.WaitAsync(cancellationToken);
            var transcriptionClock = Stopwatch.StartNew();
            string text;
            try { text = reuse ? await preview!.Value.Text.WaitAsync(cancellationToken) : await TranscribeAsync(wav, cancellationToken); }
            catch (Exception) when (reuse && !cancellationToken.IsCancellationRequested)
            {
                reuse = false;
                text = await TranscribeAsync(wav, cancellationToken);
            }
            log?.Invoke($"local_whisper_final_reused_preview={reuse}");
            using var reader = wav.Length == 0 ? null : new WaveFileReader(new MemoryStream(wav, writable: false));
            log?.Invoke($"local_whisper_final samples={reader?.Length / 2 ?? 0} chars={text.Length} capture_ms={_sessionClock?.ElapsedMilliseconds ?? 0} transcription_ms={transcriptionClock.ElapsedMilliseconds}");
            log?.Invoke($"local_whisper_finish_ms={finishClock.ElapsedMilliseconds}");
            return text;
        }
        catch (Exception ex)
        {
            log?.Invoke($"local_whisper_error type={ex.GetType().Name} duration_ms={_sessionClock?.ElapsedMilliseconds ?? 0}");
            throw;
        }
    }

    public void CancelListening()
    {
        _listening?.Cancel();
        _recognition?.Cancel();
        if (microphone.IsRecording) microphone.StopToWav();
    }

    internal static bool CanReusePreview(byte[] preview, byte[] final)
    {
        if (preview.Length < 44 || final.Length < 44) return false;
        try
        {
            using var first = new WaveFileReader(new MemoryStream(preview, writable: false));
            using var last = new WaveFileReader(new MemoryStream(final, writable: false));
            if (first.WaveFormat.Encoding != WaveFormatEncoding.Pcm || last.WaveFormat.Encoding != WaveFormatEncoding.Pcm
                || first.WaveFormat.SampleRate != 16000 || last.WaveFormat.SampleRate != 16000
                || first.WaveFormat.Channels != 1 || last.WaveFormat.Channels != 1
                || first.WaveFormat.BitsPerSample != 16 || last.WaveFormat.BitsPerSample != 16
                || first.Length < 3200 || first.Length > last.Length || last.Length > 16000 * 2 * 60) return false;
            var prefix = new byte[(int)first.Length];
            var completed = new byte[(int)last.Length];
            first.ReadExactly(prefix); last.ReadExactly(completed);
            if (!completed.AsSpan(0, prefix.Length).SequenceEqual(prefix)) return false;
            // Reuse only exact captured samples plus digital silence; quiet speech must trigger fresh recognition.
            return completed.AsSpan(prefix.Length).IndexOfAnyExcept((byte)0) < 0;
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException or ArgumentException or EndOfStreamException) { return false; }
    }

    public async Task<string> TranscribeAsync(byte[] wavBytes, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (wavBytes.Length == 0) return "";
        using var reader = new WaveFileReader(new MemoryStream(wavBytes, writable: false));
        if (reader.WaveFormat.SampleRate != 16000 || reader.WaveFormat.Channels != 1 || reader.WaveFormat.BitsPerSample != 16)
            throw new InvalidDataException("Voice input must be 16 kHz mono PCM.");
        if (reader.WaveFormat.Encoding != WaveFormatEncoding.Pcm || reader.Length > 16000 * 2 * 60)
            throw new InvalidDataException("Use a PCM voice request shorter than one minute.");
        var samples = new float[reader.Length / 2];
        var count = reader.ToSampleProvider().Read(samples, 0, samples.Length);
        double energy = 0;
        for (var i = 0; i < count; i++) energy += samples[i] * samples[i];
        // shortcut: quiet input is rejected by energy, add a speech detector if noise-only input becomes common.
        if (count < 1600 || Math.Sqrt(energy / count) < 0.001) return "";
        var inferenceClock = Stopwatch.StartNew();
        try
        {
            return await runtime.TranscribeAsync(samples, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            log?.Invoke($"local_whisper_inference_ms={inferenceClock.ElapsedMilliseconds}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelListening();
        microphone.Dispose();
        _ = Task.Run(async () =>
        {
            if (_warmup is not null) await _warmup;
            if (_previews is not null) await _previews;
            lock (_previewGate) _preview = null;
            _listening?.Dispose();
            _recognition?.Dispose();
            if (ownsRuntime) runtime.Dispose();
        });
    }
}
