using OpenClicky.Core.Speech;

namespace OpenClicky.Core.Tests;

public class SpeechPlanTests
{
    [Theory]
    [InlineData(0.5, -5)]
    [InlineData(0.75, -3)] // -2.5 rounds away from zero
    [InlineData(1, 0)]
    [InlineData(1.25, 3)]
    [InlineData(1.5, 5)]
    [InlineData(5, 10)]
    [InlineData(-3, -10)]
    [InlineData(double.NaN, 0)]
    public void SpeedMapsToSapiRate(double speed, int rate) => Assert.Equal(rate, SpeechPlan.SapiRate(speed));

    [Fact]
    public void GapOnlyBetweenSentencesOfOneReply()
    {
        Assert.Equal(TimeSpan.Zero, SpeechPlan.GapBefore(null)); // first sentence
        Assert.Equal(TimeSpan.FromMilliseconds(350), SpeechPlan.GapBefore(TimeSpan.Zero));
        Assert.Equal(TimeSpan.FromMilliseconds(250), SpeechPlan.GapBefore(TimeSpan.FromMilliseconds(100)));
        Assert.Equal(TimeSpan.Zero, SpeechPlan.GapBefore(TimeSpan.FromMilliseconds(600))); // synthesis already took longer
        Assert.Equal(TimeSpan.Zero, SpeechPlan.GapBefore(TimeSpan.FromSeconds(1.5))); // idle: a new reply
    }

    [Fact]
    public void SplitsSentencesOnTerminatorsFollowedBySpace()
    {
        Assert.Equal(["Hi!", "This is 1.5 times faster.", "Ready?", "Go"], SpeechPlan.Sentences("Hi! This is 1.5 times faster.  Ready?\nGo"));
        Assert.Equal(["Visit example.com now."], SpeechPlan.Sentences(" Visit example.com now. "));
        Assert.Empty(SpeechPlan.Sentences("   "));
    }

    [Fact]
    public void WordsSpreadEvenlyOverTheAudio()
    {
        var words = SpeechPlan.SpreadWords("one  two\tthree four", TimeSpan.FromSeconds(2));
        Assert.Equal(["one", "two", "three", "four"], words.Select(w => w.Word));
        Assert.Equal([0, 0.5, 1, 1.5], words.Select(w => w.At.TotalSeconds));
        Assert.Empty(SpeechPlan.SpreadWords("", TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void ReadsPcmWavHeadersIncludingStreamedSizes()
    {
        var wav = Wav(24_000, 1, 16, data: new byte[4_800], dataSize: 0xFFFFFFFF, extraChunk: true);
        var info = SpeechPlan.ReadWav(wav);
        Assert.NotNull(info);
        Assert.Equal((24_000, 1, 16, 4_800), (info!.SampleRate, info.Channels, info.BitsPerSample, info.DataLength));
        Assert.Equal(wav.Length - 4_800, info.DataOffset);
        Assert.Equal(TimeSpan.FromMilliseconds(100), info.Duration);

        Assert.Null(SpeechPlan.ReadWav([1, 2, 3]));
        Assert.Null(SpeechPlan.ReadWav(Wav(24_000, 1, 16, data: [], dataSize: 0, extraChunk: false, format: 3))); // not PCM
    }

    private static byte[] Wav(int rate, short channels, short bits, byte[] data, uint dataSize, bool extraChunk, short format = 1)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(0xFFFFFFFF); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write(format); w.Write(channels); w.Write(rate);
        w.Write(rate * channels * bits / 8); w.Write((short)(channels * bits / 8)); w.Write(bits);
        if (extraChunk) { w.Write("LIST"u8); w.Write(3); w.Write(new byte[] { 1, 2, 3, 0 }); } // odd size is padded
        w.Write("data"u8); w.Write(dataSize); w.Write(data);
        w.Flush();
        return ms.ToArray();
    }
}
