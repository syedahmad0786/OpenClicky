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
    /// The WaveOut device number for a Core Audio endpoint id, or -1 (the default device) when unset, unplugged, not found
    /// or ambiguous (<see cref="SpeechPlan.MatchWaveOut"/>).
    /// ponytail: WaveOut only knows devices by their 31-character name; play through WasapiOut(MMDevice) instead if two
    /// identical outputs must be told apart.
    /// </summary>
    public static int WaveOutNumber(string? endpointId)
    {
        if (string.IsNullOrWhiteSpace(endpointId)) return -1;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var endpoint = enumerator.GetDevice(endpointId);
            if (endpoint.State != DeviceState.Active) return -1;
            var products = Enumerable.Range(0, WaveOut.DeviceCount).Select(i => WaveOut.GetCapabilities(i).ProductName).ToList();
            return SpeechPlan.MatchWaveOut(endpoint.FriendlyName, products);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException or NAudio.MmException) { return -1; }
    }

    /// <summary>
    /// The endpoint replies play on (the selected one while it is active, else the default output) with its mute state and
    /// master volume (0–1); null when there is no output device or Core Audio fails.
    /// </summary>
    public static (string Id, bool Muted, float Volume)? ReadVolume(string? endpointId)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var endpoint = OpenEndpoint(enumerator, endpointId);
            var volume = endpoint.AudioEndpointVolume;
            return (endpoint.ID, volume.Mute, volume.MasterVolumeLevelScalar);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException or InvalidOperationException) { return null; }
    }

    /// <summary>Unmutes the endpoint and sets its master volume; false when it is gone or Core Audio fails.</summary>
    public static bool SetVolume(string endpointId, float level)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var endpoint = enumerator.GetDevice(endpointId);
            if (endpoint.State != DeviceState.Active) return false;
            endpoint.AudioEndpointVolume.Mute = false;
            endpoint.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(level, 0f, 1f);
            return true;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException or InvalidOperationException) { return false; }
    }

    private static MMDevice OpenEndpoint(MMDeviceEnumerator enumerator, string? endpointId)
    {
        if (!string.IsNullOrWhiteSpace(endpointId))
        {
            MMDevice? selected = null;
            try
            {
                selected = enumerator.GetDevice(endpointId);
                if (selected.State == DeviceState.Active) return selected;
                selected.Dispose();
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException) { selected?.Dispose(); } // unplugged: the default plays
        }
        return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    }

    public static Task PlayAsync(SpeechClip clip, int device, Action<string>? wordSpoken, CancellationToken cancellationToken)
    {
        var format = new WaveFormat(clip.Info.SampleRate, clip.Info.BitsPerSample, clip.Info.Channels);
        var audio = new RawSourceWaveStream(new MemoryStream(clip.Wav, clip.Info.DataOffset, clip.Info.DataLength, writable: false), format);
        return Task.Run(() => PlayCoreAsync(output => output.Init(audio), audio, device, clip.Words, wordSpoken, cancellationToken), CancellationToken.None);
    }

    public static Task PlayAsync(ISampleProvider audio, int device, CancellationToken cancellationToken) =>
        Task.Run(() => PlayCoreAsync(output => output.Init(audio), null, device, [], null, cancellationToken), CancellationToken.None);

    /// <summary>
    /// Plays to the end; cancelling stops at once. Always started on a pool thread (the public overloads use Task.Run):
    /// a WaveOutEvent created on the UI thread would post PlaybackStopped through the UI loop. Words are raised on a
    /// worker thread as playback reaches them; a throwing subscriber is ignored.
    /// </summary>
    private static async Task PlayCoreAsync(Action<WaveOutEvent> init, IDisposable? source, int device,
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
                    while (next < words.Count && words[next].At <= at) Raise(wordSpoken, words[next++].Word);
                }
                await Task.WhenAny(stopped.Task, Task.Delay(25, CancellationToken.None)).ConfigureAwait(false);
            }
            await stopped.Task.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            while (wordSpoken is not null && next < words.Count) Raise(wordSpoken, words[next++].Word); // the tail the last poll missed
        }
    }

    private static void Raise(Action<string> handler, string word)
    {
        try { handler(word); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { DebugLog.Append("speech_word_handler_error=" + ex.GetType().Name); }
    }
}
