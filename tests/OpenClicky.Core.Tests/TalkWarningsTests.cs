using OpenClicky.Core.Shell;
using OpenClicky.Core.Speech;

namespace OpenClicky.Core.Tests;

public class TalkWarningsTests
{
    [Theory]
    [InlineData(true, 0.8f, true)]
    [InlineData(false, 0f, true)]
    [InlineData(true, 0f, true)]
    [InlineData(false, float.NaN, true)]
    [InlineData(false, 0.01f, false)]
    [InlineData(false, 1f, false)]
    public void MutedSpeakerWarnsWhenMutedOrAtZero(bool muted, float volume, bool warn) =>
        Assert.Equal(warn, MutedSpeakerPolicy.ShouldWarn(muted, volume));

    [Theory]
    [InlineData(3_000, 0f, true)]
    [InlineData(10_000, 0.0099f, true)]
    [InlineData(2_999, 0f, false)]
    [InlineData(5_000, 0.01f, false)]
    [InlineData(5_000, 0.4f, false)]
    public void MicTroubleNeedsALongSilentHold(long holdMs, float maxPeak, bool warn) =>
        Assert.Equal(warn, MicTroublePolicy.ShouldWarn(holdMs, maxPeak));

    [Fact]
    public void CardsCarryTheirIdsKindsAndActions()
    {
        var muted = TalkCards.MutedSpeaker();
        Assert.Equal(("muted-speaker", NotchCardKind.MutedSpeaker, 12_000), (muted.Id, muted.Kind, muted.AutoDismissMs));
        Assert.Equal(new CardAction("volume50", "Turn volume to 50%", CardActionStyle.Primary), Assert.Single(muted.Actions));
        var mic = TalkCards.MicTrouble();
        Assert.Equal(("mic-trouble", NotchCardKind.MicTrouble, 15_000), (mic.Id, mic.Kind, mic.AutoDismissMs));
        Assert.Equal(new CardAction("micSettings", "Microphone settings", CardActionStyle.Primary), Assert.Single(mic.Actions));
    }
}
