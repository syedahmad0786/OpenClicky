using NAudio.Wave;
using OpenClicky.Core;

namespace OpenClicky.Input;

public sealed class NAudioMicrophoneRecorder : IMicrophoneRecorder
{
    private readonly object _gate = new();
    private WaveInEvent? _waveIn;
    private MemoryStream? _pcm;
    private WaveFormat? _format;
    private bool _disposed;

    public bool IsRecording
    {
        get
        {
            lock (_gate)
            {
                return _waveIn is not null;
            }
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_waveIn is not null)
            {
                return;
            }

            _pcm = new MemoryStream();
            _waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(16000, 16, 1),
                BufferMilliseconds = 50,
            };
            _format = _waveIn.WaveFormat;
            _waveIn.DataAvailable += (_, args) =>
            {
                _pcm?.Write(args.Buffer, 0, args.BytesRecorded);
            };
            _waveIn.StartRecording();
        }
    }

    public byte[] StopToWav()
    {
        lock (_gate)
        {
            if (_waveIn is null || _pcm is null || _format is null)
            {
                return Array.Empty<byte>();
            }

            _waveIn.StopRecording();
            _waveIn.Dispose();
            _waveIn = null;

            var pcmBytes = _pcm.ToArray();
            _pcm.Dispose();
            _pcm = null;
            var format = _format;
            _format = null;

            return PcmToWav(pcmBytes, format);
        }
    }

    private static byte[] PcmToWav(byte[] pcm, WaveFormat format)
    {
        using var output = new MemoryStream();
        using (var writer = new WaveFileWriter(new IgnoreDisposeStream(output), format))
        {
            writer.Write(pcm, 0, pcm.Length);
        }

        return output.ToArray();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_gate)
        {
            try
            {
                _waveIn?.StopRecording();
            }
            catch
            {
                // ignore
            }

            _waveIn?.Dispose();
            _waveIn = null;
            _pcm?.Dispose();
            _pcm = null;
        }
    }

    /// <summary>WaveFileWriter disposes the stream; we need the bytes afterward.</summary>
    private sealed class IgnoreDisposeStream : Stream
    {
        private readonly Stream _inner;

        public IgnoreDisposeStream(Stream inner) => _inner = inner;

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);
        protected override void Dispose(bool disposing) { /* keep inner open */ }
    }
}

/// <summary>No-op mic for environments without audio devices.</summary>
public sealed class NullMicrophoneRecorder : IMicrophoneRecorder
{
    public bool IsRecording { get; private set; }

    public void Start() => IsRecording = true;

    public byte[] StopToWav()
    {
        IsRecording = false;
        return Array.Empty<byte>();
    }

    public void Dispose()
    {
    }
}
