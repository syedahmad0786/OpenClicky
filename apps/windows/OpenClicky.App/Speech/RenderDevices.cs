using NAudio.CoreAudioApi;
using NAudio.Wave;
using OpenClicky.Core.Speech;

namespace OpenClicky.App.Speech;

/// <summary>Synthesized audio ready to play: a PCM WAV plus when each word starts.</summary>
internal sealed record SpeechClip(byte[] Wav, WavInfo Info, IReadOnlyList<(string Word, TimeSpan At)> Words);

/// <summary>Plays audio with NAudio <see cref="WaveOutEvent"/> on the output device chosen in Settings → Voice.</summary>
internal static class RenderDevices
{
    /// <summary>
    /// The WaveOut device number for a Core Audio endpoint id, or -1 (the default device) when unset, unplugged or not
    /// found. WaveOut names are the endpoint's friendly name cut to 31 characters, so a prefix match is used.
    /// </summary>
    public static int WaveOutNumber(string? endpointId)
    {
        if (string.IsNullOrWhiteSpace(endpointId)) return -1;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var endpoint = enumerator.GetDevice(endpointId);
            if (endpoint.State != DeviceState.Active) return -1;
            var name = endpoint.FriendlyName;
            for (var i = 0; i < WaveOut.DeviceCount; i++)
            {
                var product = WaveOut.GetCapabilities(i).ProductName;
                if (product.Length > 0 && name.StartsWith(product, StringComparison.OrdinalIgnoreCase)) return i;
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException or NAudio.MmException) { }
        return -1;
    }

    public static Task PlayAsync(SpeechClip clip, int device, Action<string>? wordSpoken, CancellationToken cancellationToken)
    {
        var format = new WaveFormat(clip.Info.SampleRate, clip.Info.BitsPerSample, clip.Info.Channels);
        var audio = new RawSourceWaveStream(new MemoryStream(clip.Wav, clip.Info.DataOffset, clip.Info.DataLength, writable: false), format);
        return PlayAsync(output => output.Init(audio), audio, device, clip.Words, wordSpoken, cancellationToken);
    }

    public static Task PlayAsync(ISampleProvider audio, int device, CancellationToken cancellationToken) =>
        PlayAsync(output => output.Init(audio), null, device, [], null, cancellationToken);

    /// <summary>Plays to the end; cancelling stops at once. Words are raised on a worker thread as playback reaches them.</summary>
    private static async Task PlayAsync(Action<WaveOutEvent> init, IDisposable? source, int device,
        IReadOnlyList<(string Word, TimeSpan At)> words, Action<string>? wordSpoken, CancellationToken cancellationToken)
    {
        using var owned = source;
        cancellationToken.ThrowIfCancellationRequested();
        using var output = new WaveOutEvent { DeviceNumber = device, DesiredLatency = 150 };
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        output.PlaybackStopped += (_, e) => { if (e.Exception is { } error) stopped.TrySetException(error); else stopped.TrySetResult(); };
        init(output);
        output.Play();
        using (cancellationToken.Register(() => { try { output.Stop(); } catch (Exception ex) when (ex is NAudio.MmException or ObjectDisposedException) { } }))
        {
            var next = 0;
            var bytesPerSecond = (double)output.OutputWaveFormat.AverageBytesPerSecond;
            while (!stopped.Task.IsCompleted)
            {
                if (wordSpoken is not null && next < words.Count)
                {
                    var at = TimeSpan.FromSeconds(output.GetPosition() / bytesPerSecond);
                    while (next < words.Count && words[next].At <= at) wordSpoken(words[next++].Word);
                }
                await Task.WhenAny(stopped.Task, Task.Delay(25, CancellationToken.None)).ConfigureAwait(false);
            }
            await stopped.Task.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            while (wordSpoken is not null && next < words.Count) wordSpoken(words[next++].Word); // the tail the last poll missed
        }
    }
}
