using System.Text.Json;
using OpenClicky.Core.Proactive;

namespace OpenClicky.Core.Tests;

public class NudgeTrackerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 25, 10, 0, 0, TimeSpan.Zero);
    private static NudgeApp App(string id) => NudgeCatalog.Apps.Single(a => a.Id == id);
    private static readonly Func<NudgeApp, bool> NotConnected = _ => false;

    /// <summary>Samples every 2.5 s for <paramref name="seconds"/> and returns the first offset (s) at which a nudge fired.</summary>
    private static double? FirstNudge(NudgeTracker t, NudgeApp app, DateTimeOffset from, double seconds, bool canShow = true)
    {
        for (var s = 0.0; s <= seconds; s += 2.5)
            if (t.Observe(app, from.AddSeconds(s), canShow, NotConnected) is not null) return s;
        return null;
    }

    [Fact]
    public void NeedsTenSecondsOfDwell()
    {
        var t = new NudgeTracker(new NudgeState());
        Assert.Equal(10, FirstNudge(t, App("notion"), T0, 60));

        // switching apps resets credit
        t = new NudgeTracker(new NudgeState());
        Assert.Null(t.Observe(App("notion"), T0, true, NotConnected));
        Assert.Null(t.Observe(App("notion"), T0.AddSeconds(7.5), true, NotConnected));
        Assert.Null(t.Observe(App("slack"), T0.AddSeconds(10), true, NotConnected));
        Assert.Null(t.Observe(App("notion"), T0.AddSeconds(12.5), true, NotConnected));
        Assert.Null(t.Observe(null, T0.AddSeconds(15), true, NotConnected));
        Assert.Null(t.Observe(App("notion"), T0.AddSeconds(17.5), true, NotConnected));
        Assert.Null(t.Observe(App("notion"), T0.AddSeconds(20), true, NotConnected));

        // a single long gap on the same app credits at most 30 s, which is enough
        t = new NudgeTracker(new NudgeState());
        Assert.Null(t.Observe(App("notion"), T0, true, NotConnected));
        Assert.Equal("notion", t.Observe(App("notion"), T0.AddMinutes(5), true, NotConnected)?.Id);

        // a gap after a different previous app credits nothing
        t = new NudgeTracker(new NudgeState());
        Assert.Null(t.Observe(App("slack"), T0, true, NotConnected));
        Assert.Null(t.Observe(App("notion"), T0.AddMinutes(5), true, NotConnected));
    }

    [Fact]
    public void CapsThreePer24Hours()
    {
        var t = new NudgeTracker(new NudgeState());
        var at = T0;
        foreach (var id in new[] { "notion", "linear", "slack" })
        {
            Assert.Equal(10, FirstNudge(t, App(id), at, 30));
            at = at.AddMinutes(1);
        }
        Assert.Null(FirstNudge(t, App("asana"), at, 60)); // fourth, within 24 h of the first
        // the first nudge fired at T0 + 10 s, so its slot frees exactly 24 h later
        Assert.Null(t.Observe(App("asana"), T0.AddHours(24).AddSeconds(9), true, NotConnected));
        Assert.Equal("asana", t.Observe(App("asana"), T0.AddHours(24).AddSeconds(10), true, NotConnected)?.Id);
    }

    [Fact]
    public void SnoozesFor48HoursAndNever()
    {
        var state = new NudgeState();
        var t = new NudgeTracker(state);
        Assert.Equal(10, FirstNudge(t, App("notion"), T0, 30));
        var shownAt = T0.AddSeconds(10);

        // keeps dwelling: still snoozed one minute short of 48 h
        Assert.Null(t.Observe(App("notion"), shownAt.AddHours(48).AddMinutes(-2), true, NotConnected));
        Assert.Null(t.Observe(App("notion"), shownAt.AddHours(48).AddMinutes(-1), true, NotConnected));
        Assert.Equal("notion", t.Observe(App("notion"), shownAt.AddHours(48), true, NotConnected)?.Id);

        // state round-trips through JSON and a fresh tracker needs fresh dwell
        var copy = JsonSerializer.Deserialize<NudgeState>(JsonSerializer.Serialize(state))!;
        var t2 = new NudgeTracker(copy);
        var later = shownAt.AddHours(100);
        Assert.Equal(10, FirstNudge(t2, App("linear"), later, 30));

        // never
        t2.Never("notion");
        Assert.Contains("notion", copy.Never);
        Assert.Null(FirstNudge(t2, App("notion"), later.AddHours(200), 60));
        t2.Never("notion");
        Assert.Single(copy.Never);

        // connected app never nudges
        var t3 = new NudgeTracker(new NudgeState());
        for (var s = 0.0; s <= 60; s += 2.5)
            Assert.Null(t3.Observe(App("notion"), T0.AddSeconds(s), true, a => a.Id == "notion"));
    }

    [Fact]
    public void CannotShowStillCreditsAndFiresOnFirstAllowedSample()
    {
        var t = new NudgeTracker(new NudgeState());
        Assert.Null(FirstNudge(t, App("notion"), T0, 30, canShow: false));
        Assert.Equal("notion", t.Observe(App("notion"), T0.AddSeconds(32.5), true, NotConnected)?.Id);
    }
}
