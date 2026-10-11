using System.Speech.Synthesis;
using OpenClicky.Core;

namespace OpenClicky.App;

/// <summary>Local offline TTS via Windows SAPI — no API key required.</summary>
public sealed class LocalSpeechTtsClient : ITtsClient
{
    private readonly object _gate = new();
    private SpeechSynthesizer? _synth;
    public bool Enabled { get; set; } = true;

    public async Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var synth = new SpeechSynthesizer { Rate = 2 };
        synth.SetOutputToDefaultAudioDevice();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        synth.SpeakCompleted += (_, e) =>
        {
            if (e.Cancelled) completion.TrySetCanceled();
            else if (e.Error is not null) completion.TrySetException(e.Error);
            else completion.TrySetResult();
        };
        lock (_gate) _synth = synth;
        try
        {
            synth.SpeakAsync(text);
            using var registration = cancellationToken.Register(Stop);
            await completion.Task.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally { lock (_gate) { if (ReferenceEquals(_synth, synth)) _synth = null; } }
    }

    public void Stop()
    {
        lock (_gate)
        {
            try
            {
                _synth?.SpeakAsyncCancelAll();
            }
            catch
            {
                // ignore
            }
        }
    }
}
