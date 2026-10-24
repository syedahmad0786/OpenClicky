using System.Buffers.Binary;
using System.Text.RegularExpressions;

namespace OpenClicky.Core.Speech;

/// <summary>PCM layout of a WAV file: where the samples are and how to play them.</summary>
public sealed record WavInfo(int SampleRate, int Channels, int BitsPerSample, int DataOffset, int DataLength)
{
    public int BytesPerSecond => SampleRate * Channels * BitsPerSample / 8;
    public TimeSpan Duration => TimeSpan.FromSeconds((double)DataLength / BytesPerSecond);
}

/// <summary>Pure speech timing rules: SAPI rate, the gap between sentences, word timings and WAV layout.</summary>
public static partial class SpeechPlan
{
    public static readonly TimeSpan SentenceGap = TimeSpan.FromMilliseconds(350);

    /// <summary>Longer than this since the last sentence ended means a new reply, which starts without a gap.</summary>
    public static readonly TimeSpan IdleAfter = TimeSpan.FromSeconds(1);

    /// <summary>round((speed − 1) × 10) clamped to −10…10; NaN counts as normal speed.</summary>
    public static int SapiRate(double speed) =>
        double.IsNaN(speed) ? 0 : (int)Math.Clamp(Math.Round((speed - 1) * 10, MidpointRounding.AwayFromZero), -10, 10);

    /// <summary>How long to wait before the next sentence, given the time since the previous one ended (null: none yet).</summary>
    public static TimeSpan GapBefore(TimeSpan? sinceLastEnd) =>
        sinceLastEnd is not { } idle || idle > IdleAfter || idle >= SentenceGap ? TimeSpan.Zero : SentenceGap - idle;

    /// <summary>Sentences ending in . ! or ? followed by whitespace (so "1.5" and "example.com" stay whole), trimmed.</summary>
    public static IReadOnlyList<string> Sentences(string text) =>
        SentenceEnd().Split(text).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

    /// <summary>Whitespace-separated words placed evenly across <paramref name="duration"/> (for audio without word timings).</summary>
    public static IReadOnlyList<(string Word, TimeSpan At)> SpreadWords(string text, TimeSpan duration)
    {
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return words.Select((w, i) => (w, duration * i / words.Length)).ToList();
    }

    /// <summary>The PCM format and data chunk of a WAV file, or null when it is not PCM WAV. A data size larger than the file
    /// (streamed WAV writes 0xFFFFFFFF) is cut to what is there.</summary>
    public static WavInfo? ReadWav(ReadOnlySpan<byte> wav)
    {
        if (wav.Length < 12 || !wav[..4].SequenceEqual("RIFF"u8) || !wav[8..12].SequenceEqual("WAVE"u8)) return null;
        (int Rate, int Channels, int Bits)? format = null;
        var at = 12;
        while (at + 8 <= wav.Length)
        {
            var id = wav.Slice(at, 4);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(wav.Slice(at + 4, 4));
            var body = at + 8;
            if (id.SequenceEqual("fmt "u8))
            {
                if (size < 16 || body + 16 > wav.Length || BinaryPrimitives.ReadInt16LittleEndian(wav.Slice(body, 2)) != 1) return null;
                format = (BinaryPrimitives.ReadInt32LittleEndian(wav.Slice(body + 4, 4)), BinaryPrimitives.ReadInt16LittleEndian(wav.Slice(body + 2, 2)),
                    BinaryPrimitives.ReadInt16LittleEndian(wav.Slice(body + 14, 2)));
            }
            else if (id.SequenceEqual("data"u8))
            {
                if (format is not { } f || f.Rate <= 0 || f.Channels <= 0 || f.Bits <= 0) return null;
                var length = (int)Math.Min(size, (uint)(wav.Length - body));
                var frame = f.Channels * f.Bits / 8;
                return new WavInfo(f.Rate, f.Channels, f.Bits, body, length - length % Math.Max(frame, 1));
            }
            if (size > (uint)(wav.Length - body)) return null;
            at = body + (int)size + (int)(size & 1);
        }
        return null;
    }

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceEnd();
}
