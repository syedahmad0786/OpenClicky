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
        Routine R(string id, string jobId, bool paused, DateTimeOffset next) => new(id, jobId, RoutineCadence.Daily, 0, 0, 0, paused, next);
        var routines = new List<Routine>
        {
            R("r1", "a", false, now.AddHours(2).ToUniversalTime()), // later today
            R("r2", "b", true, now.AddHours(1)), // paused
            R("r3", "d", false, now.AddDays(1)), // tomorrow
            R("r4", "gone", false, now.AddHours(1)), // job deleted
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
        Assert.Equal(["Job a", "Job d"], s.RoutineTitles);
    }

    [Theory]
    [InlineData(@"C:#Apps#Zoom#Zoom.exe", 5L, 0L, @"C:\OpenClicky\OpenClicky.exe", true)]
    [InlineData(@"C:#Apps#Zoom#Zoom.exe", 5L, 9L, @"C:\OpenClicky\OpenClicky.exe", false)] // stopped
    [InlineData(@"C:#Apps#Zoom#Zoom.exe", 0L, 0L, @"C:\OpenClicky\OpenClicky.exe", false)] // never used
    [InlineData(@"C:#Apps#Zoom#Zoom.exe", null, null, @"C:\OpenClicky\OpenClicky.exe", false)] // no values (e.g. the NonPackaged key)
    [InlineData(@"c:#openclicky#OPENCLICKY.EXE", 5L, 0L, @"C:\OpenClicky\OpenClicky.exe", false)] // our own recording
    [InlineData(@"MicrosoftTeams_8wekyb3d8bbwe", 5L, 0L, null, true)]
    public void MicInUseByAnotherApp(string name, long? start, long? stop, string? ownExe, bool expected) =>
        Assert.Equal(expected, ProactiveHostInputs.MicInUseByOther(name, start, stop, ownExe));
}
