using OpenClicky.Core.Work;
using OpenClicky.Core.Proactive;

namespace OpenClicky.Core.Tests;

public class ProactiveHostInputsTests
{
    [Fact]
    public void SummarisesJobsAndRoutinesForTheLocalDay()
    {
        var now = new DateTimeOffset(2026, 10, 26, 9, 30, 0, TimeSpan.FromHours(2));
        WorkJob Job(string id, string status, DateTimeOffset created, DateTimeOffset? lastRun = null) =>
            new(id, "openclicky", "Job " + id, "p", created, status, "blue", "", lastRun);
        var jobs = new List<WorkJob>
        {
            Job("a", WorkJobStates.NeedsYou, now.AddDays(-3)),
            Job("b", WorkJobStates.Completed, now.AddDays(-5), new DateTimeOffset(now.Date.AddDays(-1).AddHours(1), now.Offset)), // yesterday 01:00 local: counts
            Job("c", WorkJobStates.Completed, now.AddDays(-5), new DateTimeOffset(now.Date.AddDays(-1), now.Offset).AddMinutes(-1)), // before yesterday's midnight
            Job("d", WorkJobStates.Ready, now.AddMinutes(-1)),
        };
        for (var i = 0; i < 10; i++) jobs.Add(Job("old" + i, WorkJobStates.Ready, now.AddDays(-30 - i)));
        jobs.Add(Job("A", WorkJobStates.Ready, now.AddDays(-60))); // "Job A": same title as "Job a" but for case
        Routine R(string id, string jobId, bool paused, DateTimeOffset next) => new(id, jobId, RoutineCadence.Daily, 0, 0, 0, paused, next);
        var routines = new List<Routine>
        {
            R("r1", "a", false, now.AddHours(2).ToUniversalTime()), // later today
            R("r2", "b", true, now.AddHours(1)), // paused
            R("r3", "d", false, now.AddDays(1)), // tomorrow
            R("r4", "gone", false, now.AddHours(1)), // job deleted
            R("r5", "a", false, now.AddHours(-1)), // due earlier today: not in today's slots; a second routine for job a
            R("r6", "A", false, now.AddDays(2)), // a title that differs only by case
        };

        var s = ProactiveHostInputs.Summarise(jobs, routines, now);

        Assert.Equal(1, s.JobsNeedYou);
        Assert.Equal(1, s.JobsFinished);
        Assert.Equal(10, s.RecentJobTitles.Count);
        Assert.Equal("Job d", s.RecentJobTitles[0]);
        var slot = Assert.Single(s.RoutinesToday);
        Assert.Equal("Job a", slot.Title);
        Assert.Equal(now.AddHours(2), slot.At);
        Assert.Equal(now.Offset, slot.At.Offset);
        Assert.Equal(["Job a", "Job d"], s.RoutineTitles); // one per title, ignoring case
    }

    [Theory]
    [InlineData(true, 4, false, false, "Paused")] // the tray's Pause wins over everything
    [InlineData(false, 5, false, false, null)]
    [InlineData(false, 5, true, false, "On a call")]
    [InlineData(false, 4, false, true, "Presenting")] // presenting counts even with our window in front
    [InlineData(false, 3, false, false, "Full screen")]
    [InlineData(false, 3, false, true, null)] // the notch itself can read as full screen
    [InlineData(false, 2, false, true, null)]
    [InlineData(false, 5, true, true, "On a call")]
    public void QuietReasonUsesEsQuietStateAndThePause(bool paused, int notificationState, bool mic, bool ownWindow, string? expected) =>
        Assert.Equal(expected, ProactiveHostInputs.QuietReason(paused, QuietState.From(notificationState, mic), ownWindow));
}
