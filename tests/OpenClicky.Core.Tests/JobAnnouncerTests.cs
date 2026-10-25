using OpenClicky.Core.Shell;
using OpenClicky.Core.Work;

namespace OpenClicky.Core.Tests;

public sealed class JobAnnouncerTests
{
    private static readonly QuietState Calm = new(false, null);
    private static readonly QuietState Presenting = new(true, "Presenting");

    private static Announcement For(JobEvent e, string title = "Tidy my desktop", bool speak = true, bool bubble = true, QuietState? quiet = null, bool talkBusy = false,
        bool replyDelivered = false, bool scheduled = false) =>
        JobAnnouncer.For(e, title, "job1", speak, bubble, quiet ?? Calm, talkBusy, replyDelivered, scheduled);

    [Theory]
    [InlineData(JobEvent.Started, "On it: Tidy my desktop.")]
    [InlineData(JobEvent.Done, "Tidy my desktop is ready.")]
    [InlineData(JobEvent.NeedsYou, "Tidy my desktop needs you.")]
    [InlineData(JobEvent.Failed, "Tidy my desktop didn't finish.")]
    public void SpeechAndBubbleShareTheCopy(JobEvent e, string line)
    {
        var a = For(e);
        Assert.Equal(line, a.Speak);
        Assert.Equal(line, a.Bubble);
    }

    [Fact]
    public void StoppedSaysNothing()
    {
        var a = For(JobEvent.Stopped);
        Assert.Null(a.Speak);
        Assert.Null(a.Bubble);
        Assert.Null(a.Card);
    }

    [Fact]
    public void StartedHasNoCard() => Assert.Null(For(JobEvent.Started).Card);

    [Theory]
    [InlineData(JobEvent.Done, NotchCardKind.JobUpdate, "Ready to view", 10_000)]
    [InlineData(JobEvent.Failed, NotchCardKind.JobUpdate, "Didn't finish", 10_000)]
    [InlineData(JobEvent.NeedsYou, NotchCardKind.NeedsYou, "Needs you", null)]
    public void CardsOpenTheJob(JobEvent e, NotchCardKind kind, string body, int? dismissMs)
    {
        var card = For(e).Card!;
        Assert.Equal("job-update:job1", card.Id);
        Assert.Equal(kind, card.Kind);
        Assert.Equal("Tidy my desktop", card.Title);
        Assert.Equal(body, card.Body);
        Assert.Equal(dismissMs, card.AutoDismissMs);
        var open = Assert.Single(card.Actions);
        Assert.Equal("open", open.Id);
    }

    [Fact]
    public void QuietMutesSpeechAndBubbleButKeepsTheCard()
    {
        var a = For(JobEvent.Done, quiet: Presenting);
        Assert.Null(a.Speak);
        Assert.Null(a.Bubble);
        Assert.NotNull(a.Card);
    }

    [Fact]
    public void BusyTalkMutesSpeechOnly()
    {
        var a = For(JobEvent.Done, talkBusy: true);
        Assert.Null(a.Speak);
        Assert.NotNull(a.Bubble);
    }

    [Fact]
    public void SettingsTurnEachOff()
    {
        var a = For(JobEvent.Done, speak: false);
        Assert.Null(a.Speak);
        Assert.NotNull(a.Bubble);
        var b = For(JobEvent.Done, bubble: false);
        Assert.NotNull(b.Speak);
        Assert.Null(b.Bubble);
        Assert.NotNull(b.Card);
    }

    [Fact]
    public void LongTitlesAreCutTo80()
    {
        var a = For(JobEvent.Done, new string('x', 200));
        Assert.Equal(new string('x', 80), a.Card!.Title);
        Assert.Equal(new string('x', 80) + " is ready.", a.Speak);
    }

    [Fact]
    public void TitleLineBreaksBecomeSpacesAndBlankTitlesFallBack()
    {
        Assert.Equal("a b is ready.", For(JobEvent.Done, "  a\r\nb ").Speak);
        Assert.Equal("Your job is ready.", For(JobEvent.Done, "  ").Speak);
    }

    [Theory]
    [InlineData(JobEvent.Done)]
    [InlineData(JobEvent.Failed)]
    public void ATalkJobWhoseReplyWasDeliveredKeepsOnlyTheCard(JobEvent e)
    {
        var a = For(e, replyDelivered: true);
        Assert.Null(a.Speak);
        Assert.Null(a.Bubble);
        Assert.NotNull(a.Card);
    }

    [Theory]
    [InlineData(JobEvent.Started)]
    [InlineData(JobEvent.NeedsYou)]
    public void ADeliveredReplyDoesNotSilenceOtherEvents(JobEvent e)
    {
        var a = For(e, replyDelivered: true);
        Assert.NotNull(a.Speak);
        Assert.NotNull(a.Bubble);
    }

    [Fact]
    public void ScheduledRunsDoNotAnnounceStartedButStillAnnounceTheEnd()
    {
        var started = For(JobEvent.Started, scheduled: true);
        Assert.Null(started.Speak);
        Assert.Null(started.Bubble);
        var done = For(JobEvent.Done, scheduled: true);
        Assert.NotNull(done.Speak);
        Assert.NotNull(done.Bubble);
        Assert.NotNull(done.Card);
    }
}
