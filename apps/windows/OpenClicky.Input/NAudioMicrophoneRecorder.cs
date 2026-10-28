using NAudio.CoreAudioApi;
using NAudio.Wave;
using OpenClicky.Core;

namespace OpenClicky.Input;

public sealed record MicrophoneDevice(string? Id, string Name);

public sealed class NAudioMicrophoneRecorder(Func<string?>? selectedDevice = null) : IMicrophoneRecorder
{
    private readonly object _gate = new();
    private readonly object _bufferGate = new();
    private WasapiCapture? _capture;
    private MMDevice? _device;
    private MemoryStream? _pcm;
    private WaveFormat? _format;
    private TaskCompletionSource? _captureStopped;
    private volatile Exception? _captureError;
    private bool _disposed;

    public bool IsRecording { get { lock (_gate) return _capture is not null && _captureError is null; } }
    public float PeakLevel
    {
        get { lock (_gate) { try { return _device?.AudioMeterInformation.MasterPeakValue ?? 0; } catch { return 0; } } }
    }

    public static IReadOnlyList<MicrophoneDevice> ListDevices()
    {
        using var devices = new MMDeviceEnumerator();
        var result = new List<MicrophoneDevice> { new(null, "Windows default microphone") };
        foreach (var device in devices.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            using (device) result.Add(new(device.ID, device.FriendlyName));
        }
        return result;
    }

    public static string DeviceName(string? id)
    {
        try { using var device = OpenDevice(id); return device.FriendlyName; }
        catch { return "Microphone unavailable · choose one in Settings"; }
    }

    private static MMDevice OpenDevice(string? id)
    {
        using var devices = new MMDeviceEnumerator();
        var device = string.IsNullOrWhiteSpace(id)
            ? devices.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia) : devices.GetDevice(id);
        if (device.DataFlow == DataFlow.Capture && device.State == DeviceState.Active) return device;
        device.Dispose();
        throw new InvalidOperationException("The selected microphone is disconnected. Choose a connected microphone in Settings.");
    }

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_capture is not null) return;
            try
            {
                _device = OpenDevice(selectedDevice?.Invoke());
                // NAudio posts RecordingStopped to this context; stopping on the UI thread must not wait on itself.
                _capture = WithoutSynchronizationContext(() => new WasapiCapture(_device));
                var format = _format = _capture.WaveFormat;
                var stopped = _captureStopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _captureError = null;
                var pcm = _pcm = new MemoryStream();
                _capture.DataAvailable += (_, args) =>
                {
                    lock (_bufferGate)
                        if (ReferenceEquals(_pcm, pcm))
                        {
                            if (pcm.Length + args.BytesRecorded <= (long)format.AverageBytesPerSecond * 60) pcm.Write(args.Buffer, 0, args.BytesRecorded);
                            else _captureError = new InvalidOperationException("Keep voice requests shorter than one minute.");
                        }
                };
                _capture.RecordingStopped += (_, args) =>
                {
                    if (args.Exception is not null) _captureError = args.Exception;
                    stopped.TrySetResult();
                };
                _capture.StartRecording();
            }
            catch { Release(); throw; }
        }
    }

    internal static T WithoutSynchronizationContext<T>(Func<T> create)
    {
        var context = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(null);
            return create();
        }
        finally { SynchronizationContext.SetSynchronizationContext(context); }
    }

    public byte[] SnapshotToWav()
    {
        lock (_gate)
        {
            if (_pcm is null || _format is null) return [];
            byte[] pcm;
            lock (_bufferGate) pcm = _pcm.ToArray();
            return PcmToWav(pcm, _format);
        }
    }

    public void DiscardBufferedAudio()
    {
        lock (_gate)
            lock (_bufferGate)
                if (_pcm is not null) _pcm.SetLength(0);
    }

    public byte[] StopToWav()
    {
        lock (_gate)
        {
            if (_capture is null || _pcm is null || _format is null) return [];
            try
            {
                _capture.StopRecording();
                if (_captureStopped is null || !_captureStopped.Task.Wait(TimeSpan.FromSeconds(2)))
                    throw new InvalidOperationException("The microphone did not finish stopping.");
                if (_captureError is not null) throw new InvalidOperationException("Microphone recording stopped: " + _captureError.Message, _captureError);
                byte[] pcm;
                lock (_bufferGate) pcm = _pcm.ToArray();
                return PcmToWav(pcm, _format);
            }
            finally { Release(); }
        }
    }

    internal static byte[] PcmToWav(byte[] pcm, WaveFormat format)
    {
        if (pcm.Length == 0) return [];
        using var source = new RawSourceWaveStream(new MemoryStream(pcm, writable: false), format);
        using var resampler = new MediaFoundationResampler(source, new WaveFormat(16000, 16, 1)) { ResamplerQuality = 60 };
        using var output = new MemoryStream();
        WaveFileWriter.WriteWavFileToStream(new IgnoreDisposeStream(output), resampler);
        return output.ToArray();
    }

    private void Release()
    {
        _capture?.Dispose();
        _capture = null;
        _device?.Dispose();
        _device = null;
        lock (_bufferGate) { _pcm?.Dispose(); _pcm = null; }
        _format = null;
        _captureStopped = null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                _capture?.StopRecording();
                _captureStopped?.Task.Wait(TimeSpan.FromSeconds(2));
            }
            finally { Release(); }
        }
    }

    private sealed class IgnoreDisposeStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        protected override void Dispose(bool disposing) { }
    }
}

public sealed class NullMicrophoneRecorder : IMicrophoneRecorder
{
    public bool IsRecording { get; private set; }
    public void Start() => IsRecording = true;
    public byte[] StopToWav() { IsRecording = false; return []; }
    public void Dispose() { }
}
