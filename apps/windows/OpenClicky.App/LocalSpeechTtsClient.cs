using System.Speech.Synthesis;
using OpenClicky.Core;

namespace OpenClicky.App;

/// <summary>Local offline TTS via Windows SAPI — no API key required.</summary>
public sealed class LocalSpeechTtsClient : ITtsClient
{
    private readonly object _gate = new();
    private SpeechSynthesizer? _synth;

    public Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.CompletedTask;
        }

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                _synth?.Dispose();
                _synth = new SpeechSynthesizer();
                _synth.Rate = 2;
                _synth.SetOutputToDefaultAudioDevice();
            }

            using var reg = cancellationToken.Register(() =>
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
            });

            SpeechSynthesizer? local;
            lock (_gate)
            {
                local = _synth;
            }

            local?.Speak(text);
        }, cancellationToken);
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
