using System.Diagnostics;
using System.IO;
using OpenClicky.App;
using OpenClicky.Core;
using OpenClicky.Core.Dictation;

internal static class WhisperWarmReuseCheck
{
    public static async Task<int> RunAsync()
    {
        VerifyPreviewReuseBoundary();
        await VerifyPendingPreviewReuseAsync();
        await VerifyModelSwapAsync();
        var loads = 0;
        var inferences = 0;
        var concurrent = 0;
        var maximumConcurrent = 0;
        using var runtime = new LocalWhisperRuntime(
            (_, _) => Task.CompletedTask,
            _ => { loads++; return new FakeEngine(() =>
            {
                var active = Interlocked.Increment(ref concurrent);
                maximumConcurrent = Math.Max(maximumConcurrent, active);
                Interlocked.Increment(ref inferences);
                return () => Interlocked.Decrement(ref concurrent);
            }); });

        var warm = Stopwatch.StartNew();
        await runtime.WarmAsync(WhisperModels.English);
        var warmMs = warm.ElapsedMilliseconds;
        var inference = Stopwatch.StartNew();
        var first = runtime.TranscribeAsync(WhisperModels.English, [0.2f, 0.1f], English);
        var second = runtime.TranscribeAsync(WhisperModels.English, [0.3f, 0.1f], English);
        var results = await Task.WhenAll(first, second);
        var inferenceMs = inference.ElapsedMilliseconds;

        if (loads != 1 || inferences != 2 || maximumConcurrent != 1 || results is not ["synthetic transcript", "synthetic transcript"])
            throw new InvalidOperationException($"Expected one model load and serialized two-turn inference; loads={loads} inferences={inferences} maximumConcurrent={maximumConcurrent}.");
        await VerifyDisposeRejectsQueuedInferenceAsync();
        Console.WriteLine($"PASS shared Whisper runtime: synthetic warm_ms={warmMs} two_inference_ms={inferenceMs} model_loads={loads} inferences={inferences} maximum_concurrent={maximumConcurrent}. No microphone or user audio was used.");
        return 0;
    }

    private static readonly WhisperDecodeOptions English = new("en", null);

    /// <summary>A second model replaces the first (disposed before the new one loads), and each call's language and prompt reach the engine.</summary>
    private static async Task VerifyModelSwapAsync()
    {
        var engines = new List<RecordingEngine>();
        using (var runtime = new LocalWhisperRuntime((_, _) => Task.CompletedTask, model =>
        {
            if (engines.Any(e => !e.Disposed)) throw new InvalidOperationException("A second Whisper model was loaded while another was in memory.");
            var engine = new RecordingEngine(model);
            engines.Add(engine);
            return engine;
        }))
        {
            await runtime.WarmAsync(WhisperModels.English);
            if (!runtime.IsWarmFor(WhisperModels.English) || runtime.IsWarmFor(WhisperModels.Multilingual))
                throw new InvalidOperationException("Warm state does not follow the loaded model.");
            await runtime.TranscribeAsync(WhisperModels.English, [0.1f], English);
            await runtime.TranscribeAsync(WhisperModels.Multilingual, [0.1f], new("auto", "OpenClicky, Ahmad"));
            await runtime.TranscribeAsync(WhisperModels.Multilingual, [0.1f], new("ur", null));
            if (engines.Count != 2 || !engines[0].Disposed || engines[1].Disposed || engines[1].Model != WhisperModels.Multilingual
                || !runtime.IsWarmFor(WhisperModels.Multilingual)
                || !engines[0].Calls.SequenceEqual([English])
                || !engines[1].Calls.SequenceEqual([new WhisperDecodeOptions("auto", "OpenClicky, Ahmad"), new WhisperDecodeOptions("ur", null)]))
                throw new InvalidOperationException($"Model swap or decode options were wrong: engines={engines.Count}.");
        }
        if (!engines[1].Disposed) throw new InvalidOperationException("Disposing the runtime left the model loaded.");
        Console.WriteLine("PASS Whisper runtime keeps one model in memory, swaps on request and passes language and prompt through");
    }

    private sealed class RecordingEngine(WhisperModelInfo model) : ILocalWhisperEngine
    {
        public WhisperModelInfo Model { get; } = model;
        public List<WhisperDecodeOptions> Calls { get; } = [];
        public bool Disposed { get; private set; }
        public Task<string> TranscribeAsync(float[] samples, WhisperDecodeOptions options, CancellationToken token) { Calls.Add(options); return Task.FromResult("x"); }
        public void Dispose() => Disposed = true;
    }

    private static void VerifyPreviewReuseBoundary()
    {
        var captured = new byte[6400];
        captured[100] = 32;
        var silenceTail = new byte[9600];
        captured.CopyTo(silenceTail, 0);
        if (!LocalWhisperSttClient.CanReusePreview(Wav(captured), Wav(silenceTail)))
            throw new InvalidOperationException("An exact preview with a silent tail was not reusable.");
        silenceTail[^2] = 1;
        if (LocalWhisperSttClient.CanReusePreview(Wav(captured), Wav(silenceTail)))
            throw new InvalidOperationException("Even quiet uncaptured sound must be recognized again.");
        silenceTail[^2] = 0; silenceTail[100] = 33;
        if (LocalWhisperSttClient.CanReusePreview(Wav(captured), Wav(silenceTail))
            || LocalWhisperSttClient.CanReusePreview(Wav(captured), Wav(new byte[3200]))
            || LocalWhisperSttClient.CanReusePreview([], Wav(captured)))
            throw new InvalidOperationException("Changed, shorter or invalid capture incorrectly reused a preview.");
    }

    private static byte[] Wav(byte[] samples)
    {
        using var output = new MemoryStream();
        using (var writer = new NAudio.Wave.WaveFileWriter(output, new NAudio.Wave.WaveFormat(16000, 16, 1)))
            writer.Write(samples, 0, samples.Length);
        return output.ToArray();
    }

    private static async Task VerifyPendingPreviewReuseAsync()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var samples = new byte[6400];
        for (var i = 1; i < samples.Length; i += 2) samples[i] = 8;
        var final = new byte[9600]; samples.CopyTo(final, 0);
        var engine = new BlockingEngine(entered, release);
        using var runtime = new LocalWhisperRuntime((_, _) => Task.CompletedTask, _ => engine);
        using var client = new LocalWhisperSttClient(new PreviewRecorder(Wav(samples), Wav(final)), runtime, new(WhisperModels.Multilingual, "fr"), "Ahmad");
        var speechStarts = 0;
        client.SpeechStarted += () => Interlocked.Increment(ref speechStarts);
        client.StartListening();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var completed = client.FinishListeningAsync(CancellationToken.None);
        release.TrySetResult();
        if (await completed.WaitAsync(TimeSpan.FromSeconds(2)) != "active transcript" || engine.Calls != 1 || speechStarts != 1)
            throw new InvalidOperationException("Final transcription did not reuse the exact in-flight preview or speech onset fired incorrectly.");
        if (engine.Options != new WhisperDecodeOptions("fr", "Ahmad"))
            throw new InvalidOperationException("The preview transcription did not use the client's language and dictionary prompt.");
    }

    private sealed class PreviewRecorder(byte[] preview, byte[] final) : IMicrophoneRecorder
    {
        public bool IsRecording { get; private set; }
        public void Start() => IsRecording = true;
        public byte[] SnapshotToWav() => preview;
        public byte[] StopToWav() { IsRecording = false; return final; }
        public void Dispose() => IsRecording = false;
    }

    private static async Task VerifyDisposeRejectsQueuedInferenceAsync()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new LocalWhisperRuntime((_, _) => Task.CompletedTask, _ => new BlockingEngine(entered, release));
        var active = runtime.TranscribeAsync(WhisperModels.English, [0.1f], English);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var queued = runtime.TranscribeAsync(WhisperModels.English, [0.2f], English);
        var disposing = Task.Run(runtime.Dispose);
        await WaitUntilAsync(() => runtime.IsDisposed, TimeSpan.FromSeconds(2));
        release.TrySetResult();
        await active;
        await disposing;
        try { await queued; throw new InvalidOperationException("Queued inference ran after runtime disposal began."); }
        catch (ObjectDisposedException) { }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline) { if (condition()) return; await Task.Delay(5); }
        throw new TimeoutException("Runtime disposal did not begin.");
    }

    private sealed class FakeEngine(Func<Action> enter) : ILocalWhisperEngine
    {
        public async Task<string> TranscribeAsync(float[] samples, WhisperDecodeOptions options, CancellationToken token)
        {
            var leave = enter();
            try { await Task.Delay(20, token); return "synthetic transcript"; }
            finally { leave(); }
        }
        public void Dispose() { }
    }

    private sealed class BlockingEngine(TaskCompletionSource entered, TaskCompletionSource release) : ILocalWhisperEngine
    {
        public int Calls;
        public WhisperDecodeOptions? Options;
        public async Task<string> TranscribeAsync(float[] samples, WhisperDecodeOptions options, CancellationToken token)
        {
            Options = options;
            Interlocked.Increment(ref Calls);
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
            return "active transcript";
        }
        public void Dispose() { }
    }
}
