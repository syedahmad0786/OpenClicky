using OpenClicky.Core.Proactive;
using OpenClicky.Core.Shell;

namespace OpenClicky.Core.Tests;

public class ProactiveCardsTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 25, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CopyAndCaps()
    {
        var brief = ProactiveCards.Brief("Good morning", ["a", "b"]);
        Assert.Equal(ProactiveCards.BriefId, brief.Id);
        Assert.Equal(NotchCardKind.Tip, brief.Kind);
        Assert.Equal("Good morning", brief.Title);
        Assert.Equal("a\nb", brief.Body);
        Assert.Null(brief.AutoDismissMs);
        var action = Assert.Single(brief.Actions);
        Assert.Equal(("plan", "Plan my day", CardActionStyle.Primary), (action.Id, action.Label, action.Style));

        var e = new UpcomingEvent("id", new string('x', 500) + "\nz", Start, null, false, null, null, null);
        var card = ProactiveCards.Meeting("proactive-meeting-1", e, Start.AddSeconds(-61));
        Assert.Equal("proactive-meeting-1", card.Id);
        Assert.Equal(NotchCardKind.Activity, card.Kind);
        Assert.Equal(160, card.Title.Length);
        Assert.Equal("Starts in 1:15", card.Body);
        Assert.Null(card.AutoDismissMs);

        var id = ProactiveCards.MeetingCardId(new string('e', 5000));
        Assert.Equal(id, ProactiveCards.MeetingCardId(new string('e', 5000)));
        Assert.NotEqual(id, ProactiveCards.MeetingCardId("other"));
        Assert.StartsWith(ProactiveCards.MeetingPrefix, id);
        Assert.True(id.Length <= 160);
        Assert.Equal(ProactiveCards.MeetingPrefix.Length + 16, id.Length);
    }

    [Fact]
    public void IsProactiveMatchesTheSharedPrefix()
    {
        Assert.True(ProactiveCards.IsProactive(ProactiveCards.BriefId));
        Assert.True(ProactiveCards.IsProactive(ProactiveCards.NudgePrefix + "x"));
        Assert.True(ProactiveCards.IsProactive(ProactiveCards.SuggestionPrefix + "x"));
        Assert.False(ProactiveCards.IsProactive("dictation-activity"));
    }

    [Fact]
    public void CleanStripsControlsCollapsesSpaceAndNeverSplitsASurrogatePair()
    {
        Assert.Equal("a b c", ProactiveCards.Clean("  a\r\n\tb \u0000\u0007 c  ", 50));
        Assert.Equal("ab", ProactiveCards.Clean("a‮b", 50)); // bidi override removed
        Assert.Equal("", ProactiveCards.Clean(null, 50));
        Assert.Equal("", ProactiveCards.Clean("abc", 0));
        Assert.Equal("abc", ProactiveCards.Clean("abcdef", 3));
        Assert.Equal("a", ProactiveCards.Clean("a\U0001F600", 2)); // the emoji would be split, so it is dropped
        Assert.Equal("a\U0001F600", ProactiveCards.Clean("a\U0001F600b", 3));
        Assert.Equal("a", ProactiveCards.Clean("a b", 2)); // no trailing space left behind
    }
}
