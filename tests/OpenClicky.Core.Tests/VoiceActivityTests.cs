using System.Buffers.Binary;
using OpenClicky.Core;

namespace OpenClicky.Core.Tests;

public class VoiceActivityTests
{
    [Fact]
    public void RecentSpeech_DistinguishesGeneratedVoiceFromSilence()
    {
        Assert.False(VoiceActivity.HasRecentSpeech(Wav(0)));
        Assert.True(VoiceActivity.HasRecentSpeech(Wav(1200)));
    }

    private static byte[] Wav(short amplitude)
    {
        var wav = new byte[44 + 6400];
        "RIFF"u8.CopyTo(wav); "WAVE"u8.CopyTo(wav.AsSpan(8)); "data"u8.CopyTo(wav.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(40), 6400);
        for (var i = 44; i < wav.Length; i += 2) BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(i), amplitude);
        return wav;
    }
}
