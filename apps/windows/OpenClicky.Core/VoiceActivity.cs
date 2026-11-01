using System.Buffers.Binary;

namespace OpenClicky.Core;

public static class VoiceActivity
{
    public static bool HasRecentSpeech(ReadOnlySpan<byte> wav)
    {
        if (wav.Length <= 44 || !wav[..4].SequenceEqual("RIFF"u8) || !wav[8..12].SequenceEqual("WAVE"u8)) return false;
        var data = wav.IndexOf("data"u8);
        if (data < 0 || data + 8 >= wav.Length) return false;
        var samples = wav[(data + 8)..];
        var recentBytes = Math.Min(samples.Length & ~1, 3200 * 2);
        if (recentBytes == 0) return false;
        samples = samples[^recentBytes..];
        double energy = 0;
        for (var i = 0; i < samples.Length; i += 2)
        {
            var value = BinaryPrimitives.ReadInt16LittleEndian(samples[i..]) / 32768f;
            energy += value * value;
        }
        return Math.Sqrt(energy / (samples.Length / 2)) >= 0.008;
    }
}
