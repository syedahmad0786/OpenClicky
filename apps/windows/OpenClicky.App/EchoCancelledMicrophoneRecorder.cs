using System.Runtime.InteropServices;
using WinRT;
using OpenClicky.Core;
using Windows.Devices.Enumeration;
using Windows.Media;
using Windows.Media.Audio;
using Windows.Media.Capture;
using Windows.Media.Devices;
using Windows.Media.Effects;
using Windows.Media.MediaProperties;
using Windows.Media.Render;

namespace OpenClicky.App;

/// <summary>In-memory Windows communications capture. Audio is never persisted by this class.</summary>
internal sealed class EchoCancelledMicrophoneRecorder(Func<string?> selectedDevice, Action<string>? log = null) : IMicrophoneRecorder
{
    private readonly object _gate = new();
    private readonly object _bufferGate = new();
    private readonly MemoryStream _pcm = new();
    private AudioGraph? _graph;
    private AudioDeviceInputNode? _input;
    private AudioFrameOutputNode? _output;
    private volatile Exception? _captureError;
    private bool _disposed;

    public bool IsRecording { get { lock (_gate) return _graph is not null && _captureError is null; } }
    public bool SupportsEchoCancellation { get; private set; }

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_graph is not null) return;
            // Never block a UI synchronization context on WinRT continuations.
            Task.Run(StartAsync).GetAwaiter().GetResult();
        }
    }

    private async Task StartAsync()
    {
        var requestedId = selectedDevice();
        var deviceId = string.IsNullOrWhiteSpace(requestedId)
            ? MediaDevice.GetDefaultAudioCaptureId(AudioDeviceRole.Communications)
            : requestedId;
        if (string.IsNullOrWhiteSpace(deviceId)) throw new InvalidOperationException("No communications microphone is available.");
        var device = await ResolveSelectedDeviceAsync(deviceId);
        var settings = new AudioGraphSettings(AudioRenderCategory.Communications)
        {
            QuantumSizeSelectionMode = QuantumSizeSelectionMode.SystemDefault
        };
        var graphResult = await AudioGraph.CreateAsync(settings);
        if (graphResult.Status != AudioGraphCreationStatus.Success)
            throw new InvalidOperationException($"Windows communications audio is unavailable ({graphResult.Status}).");
        var graph = graphResult.Graph;
        try
        {
            // AudioGraph frame nodes expose samples as IEEE float even when PCM16 is requested.
            var format = AudioEncodingProperties.CreatePcm(16000, 1, 32);
            format.Subtype = MediaEncodingSubtypes.Float;
            var inputResult = await graph.CreateDeviceInputNodeAsync(MediaCategory.Communications, graph.EncodingProperties, device);
            if (inputResult.Status != AudioDeviceNodeCreationStatus.Success)
                throw new InvalidOperationException($"The selected microphone cannot use communications processing ({inputResult.Status}).");
            var output = graph.CreateFrameOutputNode(format);
            var actual = output.EncodingProperties;
            if (!string.Equals(actual.Subtype, MediaEncodingSubtypes.Float, StringComparison.OrdinalIgnoreCase)
                || actual.SampleRate != 16000 || actual.ChannelCount != 1 || actual.BitsPerSample != 32)
                throw new InvalidOperationException($"Windows returned an unsupported microphone frame format ({actual.Subtype}, {actual.SampleRate} Hz, {actual.ChannelCount} channel, {actual.BitsPerSample} bit).");
            inputResult.DeviceInputNode.AddOutgoingConnection(output);
            lock (_bufferGate) _pcm.SetLength(0);
            _captureError = null;
            graph.QuantumStarted += OnQuantumStarted;
            _graph = graph;
            _input = inputResult.DeviceInputNode;
            _output = output;
            graph.Start();

            try
            {
                var effects = AudioEffectsManager.CreateAudioCaptureEffectsManager(device.Id, MediaCategory.Communications)
                    .GetAudioCaptureEffects();
                SupportsEchoCancellation = effects.Any(effect => effect.AudioEffectType == AudioEffectType.AcousticEchoCancellation);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                SupportsEchoCancellation = false;
                log?.Invoke($"microphone_aec_probe_error={ex.GetType().Name}");
            }
            log?.Invoke($"microphone_aec={(SupportsEchoCancellation ? "active" : "unavailable")}");
        }
        catch
        {
            graph.Dispose();
            _graph = null;
            _input = null;
            _output = null;
            throw;
        }
    }

    private static async Task<DeviceInformation> ResolveSelectedDeviceAsync(string endpointId)
    {
        var devices = await DeviceInformation.FindAllAsync(MediaDevice.GetAudioCaptureSelector());
        var matches = devices.Where(device => string.Equals(device.Id, endpointId, StringComparison.OrdinalIgnoreCase)
            || device.Id.Contains(endpointId, StringComparison.OrdinalIgnoreCase)).ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException("The selected microphone is not available to Windows communications audio."),
            _ => throw new InvalidOperationException("Windows returned more than one communications device for the selected microphone."),
        };
    }

    private unsafe void OnQuantumStarted(AudioGraph sender, object args)
    {
        try
        {
            using var frame = _output?.GetFrame();
            if (frame is null) return;
            using var buffer = frame.LockBuffer(AudioBufferAccessMode.Read);
            using var reference = buffer.CreateReference();
            reference.As<IMemoryBufferByteAccess>().GetBuffer(out var data, out var capacity);
            var inputBytes = (int)Math.Min(buffer.Length, capacity);
            var samples = inputBytes / sizeof(float);
            if (samples <= 0) return;
            var bytes = new byte[samples * sizeof(short)];
            fixed (byte* target = bytes)
            {
                var source = (float*)data;
                var output = (short*)target;
                for (var i = 0; i < samples; i++)
                    output[i] = (short)(Math.Clamp(source[i], -1f, 1f) * short.MaxValue);
            }
            lock (_bufferGate)
            {
                if (_graph is null) return;
                if (_pcm.Length + bytes.Length > 16000L * 2 * 60) throw new InvalidOperationException("Keep voice requests shorter than one minute.");
                _pcm.Write(bytes);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _captureError = ex;
            log?.Invoke($"microphone_frame_error={ex.GetType().Name}");
        }
    }

    public byte[] SnapshotToWav()
    {
        if (_captureError is { } error) throw new InvalidOperationException("Microphone capture stopped: " + error.Message, error);
        lock (_bufferGate) return MakeWav(_pcm.ToArray());
    }
    public void DiscardBufferedAudio() { lock (_bufferGate) _pcm.SetLength(0); }
    public byte[] StopToWav()
    {
        lock (_gate)
        {
            var error = _captureError;
            byte[] wav;
            lock (_bufferGate) wav = MakeWav(_pcm.ToArray());
            Release();
            if (error is not null) throw new InvalidOperationException("Microphone capture stopped: " + error.Message, error);
            return wav;
        }
    }

    private void Release()
    {
        var graph = _graph;
        _graph = null;
        if (graph is not null) { graph.Stop(); graph.QuantumStarted -= OnQuantumStarted; }
        _input?.Dispose();
        _output?.Dispose();
        graph?.Dispose();
        _input = null;
        _output = null;
        lock (_bufferGate) _pcm.SetLength(0);
        SupportsEchoCancellation = false;
        _captureError = null;
    }

    private static byte[] MakeWav(byte[] pcm)
    {
        if (pcm.Length == 0) return [];
        using var stream = new MemoryStream(pcm.Length + 44);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8); writer.Write(pcm.Length + 36); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(16000);
        writer.Write(32000); writer.Write((short)2); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(pcm.Length); writer.Write(pcm);
        return stream.ToArray();
    }

    public void Dispose() { lock (_gate) { if (_disposed) return; Release(); _pcm.Dispose(); _disposed = true; } }

    [ComImport, Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private unsafe interface IMemoryBufferByteAccess { void GetBuffer(out byte* buffer, out uint capacity); }

}
