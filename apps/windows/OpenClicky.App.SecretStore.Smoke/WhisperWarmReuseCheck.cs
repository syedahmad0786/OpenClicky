using System.Diagnostics;
using System.IO;
using OpenClicky.App;
using OpenClicky.Core;

internal static class WhisperWarmReuseCheck
{
    public static async Task<int> RunAsync()
    {
        VerifyPreviewReuseBoundary();
        await VerifyPendingPreviewReuseAsync();
        var loads = 0;
        var inferences = 0;
        var concurrent = 0;
        var maximumConcurrent = 0;
        using var runtime = new LocalWhisperRuntime(
            _ => Task.CompletedTask,
            () => { loads++; return new FakeEngine(() =>
            {
                var active = Interlocked.Increment(ref concurrent);
                maximumConcurrent = Math.Max(maximumConcurrent, active);
                Interlocked.Increment(ref inferences);
                return () => Interlocked.Decrement(ref concurrent);
            }); });

        var warm = Stopwatch.StartNew();
        await runtime.WarmAsync();
        var warmMs = warm.ElapsedMilliseconds;
        var inference = Stopwatch.StartNew();
        var first = runtime.TranscribeAsync([0.2f, 0.1f]);
        var second = runtime.TranscribeAsync([0.3f, 0.1f]);
        var results = await Task.WhenAll(first, second);
        var inferenceMs = inference.ElapsedMilliseconds;

        if (loads != 1 || inferences != 2 || maximumConcurrent != 1 || results is not ["synthetic transcript", "synthetic transcript"])
            throw new InvalidOperationException($"Expected one model load and serialized two-turn inference; loads={loads} inferences={inferences} maximumConcurrent={maximumConcurrent}.");
        await VerifyDisposeRejectsQueuedInferenceAsync();
        Console.WriteLine($"PASS shared Whisper runtime: synthetic warm_ms={warmMs} two_inference_ms={inferenceMs} model_loads={loads} inferences={inferences} maximum_concurrent={maximumConcurrent}. No microphone or user audio was used.");
        return 0;
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
        using var runtime = new LocalWhisperRuntime(_ => Task.CompletedTask, () => engine);
        using var client = new LocalWhisperSttClient(new PreviewRecorder(Wav(samples), Wav(final)), runtime);
        var speechStarts = 0;
        client.SpeechStarted += () => Interlocked.Increment(ref speechStarts);
        client.StartListening();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var completed = client.FinishListeningAsync(CancellationToken.None);
        release.TrySetResult();
        if (await completed.WaitAsync(TimeSpan.FromSeconds(2)) != "active transcript" || engine.Calls != 1 || speechStarts != 1)
            throw new InvalidOperationException("Final transcription did not reuse the exact in-flight preview or speech onset fired incorrectly.");
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
        var runtime = new LocalWhisperRuntime(_ => Task.CompletedTask, () => new BlockingEngine(entered, release));
        var active = runtime.TranscribeAsync([0.1f]);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var queued = runtime.TranscribeAsync([0.2f]);
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
        public async Task<string> TranscribeAsync(float[] samples, CancellationToken token)
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
        public async Task<string> TranscribeAsync(float[] samples, CancellationToken token)
        {
            Interlocked.Increment(ref Calls);
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
            return "active transcript";
        }
        public void Dispose() { }
    }
}
