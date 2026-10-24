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
        Assert.Null(muted.Body); // says nothing about the clipboard until the copy worked
        Assert.Equal("I copied my answer to your clipboard.", TalkCards.MutedSpeaker(copied: true).Body);
        Assert.Equal(new CardAction("volume50", "Turn volume to 50%", CardActionStyle.Primary), Assert.Single(muted.Actions));
        var mic = TalkCards.MicTrouble();
        Assert.Equal(("mic-trouble", NotchCardKind.MicTrouble, 15_000), (mic.Id, mic.Kind, mic.AutoDismissMs));
        Assert.Equal(new CardAction("micSettings", "Microphone settings", CardActionStyle.Primary), Assert.Single(mic.Actions));
    }

    [Theory]
    [InlineData(CompanionVoiceState.Listening, false)]
    [InlineData(CompanionVoiceState.Processing, false)] // streaming: the next sentence is still being written
    [InlineData(CompanionVoiceState.Responding, true)]
    [InlineData(CompanionVoiceState.Pointing, true)] // annotated replies keep the turn open for seconds after speech
    [InlineData(CompanionVoiceState.Idle, true)]
    public void CaptionsClearWhenASentenceEndsAfterTheReplyIsComplete(CompanionVoiceState state, bool clear) =>
        Assert.Equal(clear, CaptionsPolicy.ClearOnSpeechEnded(state));

    [Theory]
    [InlineData(CompanionVoiceState.Listening, true)] // a barge-in must not append the new reply to the old words
    [InlineData(CompanionVoiceState.Idle, true)]
    [InlineData(CompanionVoiceState.Processing, false)]
    [InlineData(CompanionVoiceState.Responding, false)]
    [InlineData(CompanionVoiceState.Pointing, false)]
    public void CaptionsClearOnListeningAndIdle(CompanionVoiceState state, bool clear) =>
        Assert.Equal(clear, CaptionsPolicy.ClearOnState(state));
}
