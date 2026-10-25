using OpenClicky.Core.Proactive;

namespace OpenClicky.Core.Tests;

public class MeetingCountdownTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 25, 10, 0, 0, TimeSpan.Zero);
    private static UpcomingEvent Ev(string id, DateTimeOffset at, bool allDay = false, string? url = null, string? link = null) =>
        new(id, id, at, null, allDay, url, link, null);
    private static readonly IReadOnlyDictionary<string, DateTimeOffset> None = new Dictionary<string, DateTimeOffset>();

    [Fact]
    public void ShowsFrom75sUntil2MinAfter()
    {
        var e = Ev("a", Start);
        IReadOnlyList<UpcomingEvent> list = [e];
        Assert.Null(MeetingCountdown.Current(list, Start.AddSeconds(-76), None));
        Assert.Same(e, MeetingCountdown.Current(list, Start.AddSeconds(-75), None));
        Assert.Same(e, MeetingCountdown.Current(list, Start.AddSeconds(119), None));
        Assert.Null(MeetingCountdown.Current(list, Start.AddSeconds(120), None));

        Assert.Null(MeetingCountdown.Current([Ev("d", Start, allDay: true)], Start, None));
        Assert.Null(MeetingCountdown.Current(list, Start, new Dictionary<string, DateTimeOffset> { ["a"] = Start }));

        var earlier = Ev("early", Start.AddSeconds(-30));
        Assert.Same(earlier, MeetingCountdown.Current([e, earlier], Start.AddSeconds(-10), None));
    }

    [Fact]
    public void DismissalIsPerOccurrence()
    {
        // A recurring series can reuse one event id: dismissing today's occurrence must not hide tomorrow's.
        var dismissed = new Dictionary<string, DateTimeOffset> { ["series"] = Start };
        var today = Ev("series", Start);
        var tomorrow = Ev("series", Start.AddDays(1));
        Assert.Null(MeetingCountdown.Current([today], Start, dismissed));
        Assert.Same(tomorrow, MeetingCountdown.Current([tomorrow], Start.AddDays(1), dismissed));
    }

    [Theory]
    [InlineData(75, "Starts in 1:15")]
    [InlineData(61, "Starts in 1:15")]
    [InlineData(60, "Starts in 1:00")]
    [InlineData(1, "Starts in 0:15")]
    [InlineData(0, "Starting now")]
    [InlineData(-59, "Starting now")]
    [InlineData(-60, "Started 1 min ago")]
    [InlineData(-119, "Started 1 min ago")]
    public void TextInFifteenSecondSteps(int secondsUntilStart, string expected) =>
        Assert.Equal(expected, MeetingCountdown.Text(Start, Start.AddSeconds(-secondsUntilStart)));

    [Fact]
    public void UnsafeUrlsGiveNoAction()
    {
        Assert.Equal("https://meet.example.com/abc", MeetingCountdown.SafeUrl("https://meet.example.com/abc"));
        foreach (var bad in new[] { "http://meet.example.com/abc", "javascript:alert(1)", "file:///c:/x", "https://user:pw@x.com/", "/relative", "meet.example.com", "", "  ", null })
            Assert.Null(MeetingCountdown.SafeUrl(bad));

        var now = Start.AddSeconds(-10);
        var join = ProactiveCards.Meeting("c", Ev("a", Start, url: "https://meet.example.com/abc", link: "https://cal.example.com/e"), now);
        Assert.Equal(["join"], join.Actions.Select(a => a.Id));
        Assert.Equal("Join", join.Actions[0].Label);
        var open = ProactiveCards.Meeting("c", Ev("a", Start, url: "http://bad", link: "https://cal.example.com/e"), now);
        Assert.Equal(["open"], open.Actions.Select(a => a.Id));
        Assert.Equal("Open", open.Actions[0].Label);
        Assert.Empty(ProactiveCards.Meeting("c", Ev("a", Start, url: "javascript:x", link: "file:///x"), now).Actions);
    }
}
