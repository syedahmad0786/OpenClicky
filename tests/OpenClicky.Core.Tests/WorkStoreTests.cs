using OpenClicky.Core;

namespace OpenClicky.Core.Tests;

public class WorkStoreTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    [Fact]
    public void CompanionsJobsAndRoutines_PersistWithFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "openclicky-work-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new WorkStore(Path.Combine(root, "openclicky.db"), Path.Combine(root, "companions"));
            Assert.Equal(WorkStore.DefaultCompanionSlug, Assert.Single(store.Companions()).Slug);

            var writer = store.CreateCompanion("Research Writer", "Writes sourced briefs.");
            var twin = store.CreateCompanion("Research Writer", "");
            Assert.Equal("research-writer", writer.Slug);
            Assert.Equal("research-writer-2", twin.Slug);
            Assert.Contains("Writes sourced briefs.", store.ReadNotes(writer.Slug));
            store.AppendNote(writer.Slug, "Prefers CSV");
            Assert.Contains("Prefers CSV", store.ReadNotes(writer.Slug));

            var job = store.CreateJob(writer.Slug, "Weekly brief", "Summarize this week's AI news.");
            Assert.True(Directory.Exists(store.OutputFolder(job)));
            File.WriteAllText(Path.Combine(store.OutputFolder(job), "brief.md"), "x");
            Assert.Equal("brief.md", Assert.Single(store.Outputs(job)));
            store.RecordJobRun(job.Id, AgentProgressStates.Completed, "Wrote the brief.", DateTimeOffset.UtcNow);
            Assert.Equal("Wrote the brief.", store.GetJob(job.Id).Summary);

            var now = new DateTimeOffset(2026, 10, 24, 8, 0, 0, TimeSpan.Zero);
            var routine = store.AddRoutine(job.Id, RoutineCadence.Daily, 9 * 60, 0, 0, now, Utc);
            Assert.Equal(now.AddHours(1), Assert.Single(store.Routines(job.Id)).NextRunAt);
            store.SaveRoutine(routine with { IsPaused = true });
            Assert.True(store.Routines().Single().IsPaused);

            store.UpdateCompanion(twin.Slug, "Archive me", "", isArchived: true);
            Assert.DoesNotContain(store.Companions(), c => c.Slug == twin.Slug);
            Assert.Throws<InvalidOperationException>(() => store.UpdateCompanion(WorkStore.DefaultCompanionSlug, "x", "", true));

            store.DeleteJob(job.Id);
            Assert.Empty(store.Jobs());
            Assert.Empty(store.Routines());
            Assert.True(File.Exists(Path.Combine(store.OutputFolder(job), "brief.md")));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Schedule_ComputesDailyWeeklyAndIntervalRuns()
    {
        var saturday = new DateTimeOffset(2026, 10, 24, 10, 0, 0, TimeSpan.Zero);
        var daily = Routine(RoutineCadence.Daily, minute: 9 * 60);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero), RoutineSchedule.NextAfter(daily, saturday, Utc));

        var weekdays = Routine(RoutineCadence.Weekly, minute: 9 * 60, mask: 0b0111110);
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.Zero), RoutineSchedule.NextAfter(weekdays, saturday, Utc));

        var interval = Routine(RoutineCadence.Interval, interval: 90);
        Assert.Equal(saturday.AddMinutes(90), RoutineSchedule.NextAfter(interval, saturday, Utc));

        Assert.Throws<ArgumentOutOfRangeException>(() => RoutineSchedule.Validate(Routine(RoutineCadence.Weekly, mask: 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => RoutineSchedule.Validate(Routine(RoutineCadence.Interval, interval: 1)));
        Assert.Equal("Mon, Tue, Wed, Thu, Fri at 09:00", RoutineSchedule.Describe(weekdays));
    }

    [Fact]
    public void Schedule_UsesLocalWallClockAcrossOffsets()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("PKT", TimeSpan.FromHours(5), "PKT", "PKT");
        var daily = Routine(RoutineCadence.Daily, minute: 8 * 60);
        var next = RoutineSchedule.NextAfter(daily, new DateTimeOffset(2026, 10, 24, 4, 0, 0, TimeSpan.Zero), zone);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 3, 0, 0, TimeSpan.Zero), next.ToUniversalTime());
    }

    [Fact]
    public void AfterRun_RetriesThenFallsBackAndNeverBurstsMissedRuns()
    {
        var finished = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
        var routine = Routine(RoutineCadence.Daily, minute: 9 * 60) with { NextRunAt = finished.AddDays(-5) };
        Assert.True(RoutineSchedule.IsDue(routine, finished));

        var ok = RoutineSchedule.AfterRun(routine, true, "done", finished, Utc);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero), ok.NextRunAt);
        Assert.False(RoutineSchedule.IsDue(ok, finished));

        var failed = routine;
        for (var attempt = 1; attempt <= RoutineSchedule.MaxRetries; attempt++)
        {
            failed = RoutineSchedule.AfterRun(failed, false, "error", finished, Utc);
            Assert.Equal(finished + RoutineSchedule.RetryDelay * attempt, failed.NextRunAt);
        }
        failed = RoutineSchedule.AfterRun(failed, false, "error", finished, Utc);
        Assert.Equal(ok.NextRunAt, failed.NextRunAt);
        Assert.False(RoutineSchedule.IsDue(failed with { IsPaused = true, NextRunAt = finished }, finished));
    }

    [Fact]
    public void JobPrompt_CarriesRoleNotesFollowUpAndOutputs()
    {
        var companion = new Companion("writer", "Writer", "Writes briefs.", DateTimeOffset.UtcNow);
        var job = new WorkJob("1", "writer", "Brief", "Write it.", DateTimeOffset.UtcNow, WorkJobStates.Ready, Summary: "Wrote v1.");
        var prompt = JobPrompt.Build(companion, "# Writer\n- likes CSV", job, ["v1.md"], followUp: "Shorter please", scheduled: true);
        Assert.Contains("Writes briefs.", prompt);
        Assert.Contains("likes CSV", prompt);
        Assert.Contains("Wrote v1.", prompt);
        Assert.Contains("Shorter please", prompt);
        Assert.Contains("v1.md", prompt);
        Assert.Contains("schedule", prompt);
        Assert.Equal("Done, saved brief.md.", JobPrompt.FirstSentence("Done, saved brief.md. It has three sections."));
        Assert.Equal("No ending", JobPrompt.FirstSentence("No ending"));
    }

    private static Routine Routine(RoutineCadence cadence, int minute = 0, int mask = 0, int interval = 0) =>
        new("r", "j", cadence, minute, mask, interval, false, DateTimeOffset.UnixEpoch);
}
