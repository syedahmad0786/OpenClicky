using OpenClicky.Core.Proactive;

namespace OpenClicky.Core.Tests;

public class MorningBriefTests
{
    private static readonly TimeSpan Utc = TimeSpan.Zero;
    private static DateTimeOffset At(int h, int m = 0, int day = 25) => new(2026, 10, day, h, m, 0, Utc);
    private static UpcomingEvent Ev(string title, DateTimeOffset at, bool allDay = false) =>
        new(title, title, at, null, allDay, null, null, null);

    [Fact]
    public void DueOnlyInWindowOncePerDayWhenActive()
    {
        static DateTimeOffset? Active(DateTimeOffset now) => now.AddSeconds(-5);
        Assert.False(MorningBrief.IsDue(At(6, 59), null, Active(At(6, 59))));
        Assert.True(MorningBrief.IsDue(At(7), null, Active(At(7))));
        Assert.True(MorningBrief.IsDue(At(11, 59), null, Active(At(11, 59))));
        Assert.False(MorningBrief.IsDue(At(12), null, Active(At(12))));

        var now = At(9);
        Assert.True(MorningBrief.IsDue(now, null, now.AddSeconds(-30)));
        Assert.False(MorningBrief.IsDue(now, null, now.AddSeconds(-31)));
        Assert.False(MorningBrief.IsDue(now, null, null));

        Assert.False(MorningBrief.IsDue(now, MorningBrief.DayKey(now), now));
        Assert.True(MorningBrief.IsDue(now, MorningBrief.DayKey(now.AddDays(-1)), now));
        Assert.Equal("2026-10-25", MorningBrief.DayKey(now));
    }

    [Fact]
    public void LinesCoverEachSource()
    {
        var now = At(8);
        IReadOnlyList<string> Lines(IReadOnlyList<UpcomingEvent>? today = null, int need = 0, int done = 0, params RoutineSlot[] routines) =>
            MorningBrief.Lines(now, today, need, done, routines);

        Assert.Equal(["No meetings on your calendar today."], Lines([]));
        Assert.Equal(["1 meeting today: Standup at 9:30."], Lines([Ev("Standup", At(9, 30))]));
        Assert.Equal(["3 meetings today. First: Standup at 9:30."],
            Lines([Ev("Lunch", At(12)), Ev("Holiday", At(0), allDay: true), Ev("Standup", At(9, 30)), Ev("Review", At(15))]));
        Assert.Equal(["1 job needs you."], Lines(need: 1));
        Assert.Equal(["2 jobs need you."], Lines(need: 2));
        Assert.Equal(["1 job finished since yesterday."], Lines(done: 1));
        Assert.Equal(["3 jobs finished since yesterday."], Lines(done: 3));
        Assert.Equal(["Routine today: Inbox sweep at 14:00."], Lines(routines: new RoutineSlot("Inbox sweep", At(14))));
        Assert.Equal(["2 routines run today. Next: Inbox sweep at 14:00."],
            Lines(routines: [new RoutineSlot("Backup", At(18)), new RoutineSlot("Inbox sweep", At(14))]));
        Assert.Equal(["Nothing on your plate yet."], Lines());

        var all = Lines([Ev("Standup", At(9, 30))], 1, 1, new RoutineSlot("Inbox sweep", At(14)));
        Assert.Equal(4, all.Count);
        Assert.DoesNotContain("Nothing on your plate yet.", all);
    }

    [Fact]
    public void AllDayEventsOnlyCountAsNoMeetings() =>
        Assert.Equal(["No meetings on your calendar today."],
            MorningBrief.Lines(At(8), [Ev("Holiday", At(0), allDay: true)], 0, 0, []));

    [Fact]
    public void TimesUseThePassedOffsetAndEventTitlesAreCleaned()
    {
        var now = new DateTimeOffset(2026, 10, 25, 8, 0, 0, TimeSpan.FromHours(3));
        var start = new DateTimeOffset(2026, 10, 25, 6, 30, 0, Utc); // 9:30 at +03:00
        Assert.Equal(["1 meeting today: Stand up at 9:30."], MorningBrief.Lines(now, [Ev("Stand\n\tup\u0007", start)], 0, 0, []));
    }

    [Fact]
    public void TitleAndPlanPrompt()
    {
        Assert.Equal("Good morning, Ahmad", MorningBrief.Title("Ahmad"));
        Assert.Equal("Good morning", MorningBrief.Title(" "));
        Assert.Equal("Good morning", MorningBrief.Title(null));
        Assert.Equal("Help me plan my day. My morning brief:\n- a\n- b", MorningBrief.PlanPrompt(["a", "b"]));
    }
}
