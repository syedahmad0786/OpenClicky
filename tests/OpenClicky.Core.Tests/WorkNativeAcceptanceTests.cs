using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenClicky.Core.Shell;
using OpenClicky.Core.Work;

namespace OpenClicky.Core.Tests;

/// <summary>
/// E13 automated acceptance for sub-project E. Both checks run on disk in a temp folder (never %APPDATA%\OpenClicky):
/// the first drives the real <see cref="WorkStore"/> the way the app's run-finish path does and inspects the real WORK.md;
/// the second builds the shell card with the real <see cref="ShellStateBuilder"/> and compares it to the fixture the vitest
/// check renders (set OPENCLICKY_WRITE_FIXTURES=1 to rewrite that fixture).
/// </summary>
public class WorkNativeAcceptanceTests
{
    private const string Open = "<!-- openclicky:runs -->", Close = "<!-- /openclicky:runs -->";
    private static readonly DateTimeOffset T0 = new(2026, 10, 25, 9, 30, 0, TimeSpan.Zero);
    private static string Stamp(DateTimeOffset at) => at.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OpenClicky.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine([dir!.FullName, .. parts]);
    }

    /// <summary>What FinishJob does for a run that ended: parse the reply, store the result, record the run, log it.</summary>
    private static JobResult Finish(WorkStore store, WorkJob job, string status, string reply, DateTimeOffset at)
    {
        var result = JobResult.Parse(reply, store.JobFolder(job));
        var summary = status == WorkJobStates.Completed || status == WorkJobStates.NeedsYou ? result.Summary ?? result.Reply : result.Reply;
        store.RecordJobRun(job.Id, status, summary, at);
        store.SetJobResult(job.Id, result.ToJson());
        store.AppendRunLog(job, job.Prompt, status, result.Reply);
        return result;
    }

    [Fact]
    public void StoreCheck_ThreeJobsWithStructuredResults_RenderWorkMdOnDisk()
    {
        var root = Path.Combine(Path.GetTempPath(), "openclicky-e13-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new WorkStore(Path.Combine(root, "openclicky.db"), Path.Combine(root, "companions"));
            var notes = store.CreateJob(WorkStore.DefaultCompanionSlug, "Compare note-taking apps", "Research the three best note-taking apps and save a comparison");
            var flight = store.CreateJob(WorkStore.DefaultCompanionSlug, "Book the flight", "Book the cheapest flight to Berlin");
            var digest = store.CreateJob(WorkStore.DefaultCompanionSlug, "Weekly digest", "Summarise my unread email");
            Assert.Equal(["red", "blue", "yellow"], new[] { notes, flight, digest }.Select(j => j.Colour)); // E1: least-used colour, fixed tie order

            // Job 1: completed, with a saved file, a point list, a stat and next steps. One file escapes the folder and one does not exist.
            File.WriteAllText(Path.Combine(store.OutputFolder(notes), "comparison.md"), "# Notes apps");
            File.WriteAllText(Path.Combine(root, "escape.txt"), "outside the job folder");
            var reply1 = "Here is the comparison.\n```openclicky-result\n" + JsonSerializer.Serialize(new
            {
                title = "Note-taking comparison", summary = "Compared three note-taking apps and saved a table.",
                points = new[] { "Obsidian is local-first", "Notion has the best databases" },
                stat = new { value = 3, label = "apps compared" }, next = new[] { "Try Obsidian for a week" },
                files = new[] { "output/comparison.md", "..\\..\\..\\..\\escape.txt", "output/missing.md" },
            }) + "\n```\n";
            var r1 = Finish(store, notes, WorkJobStates.Completed, reply1, T0);
            // Job 2: needs the user (a sign-in step).
            var r2 = Finish(store, flight, WorkJobStates.NeedsYou, "Signed out.\n```openclicky-result\n"
                + JsonSerializer.Serialize(new { summary = "Waiting for you to sign in.", your_turn = new { step = "Sign in to the airline", page = "https://example.com/login", done_when_url_contains = "account" } })
                + "\n```", T0.AddMinutes(10));
            // Job 3: failed, no result block at all.
            Finish(store, digest, WorkJobStates.Failed, "Couldn't reach the mail page.", T0.AddMinutes(20));
            store.WriteIndex();

            // Structured result round trip: only the file inside the job folder survives.
            var stored = JobResult.FromJson(store.GetJob(notes.Id).ResultJson)!;
            Assert.Equal([Path.GetFullPath(Path.Combine(store.OutputFolder(notes), "comparison.md"))], stored.Files);
            Assert.Equal(("3", "apps compared"), (stored.Stat!.Value, stored.Stat.Label));
            Assert.Equal(["Obsidian is local-first", "Notion has the best databases"], stored.Points);
            Assert.Equal(["Try Obsidian for a week"], stored.Next);
            Assert.DoesNotContain("openclicky-result", r1.Reply);
            Assert.Equal("Sign in to the airline", r2.YourTurn!.Step);
            Assert.Contains("Asked: Research the three best", File.ReadAllText(Path.Combine(store.JobFolder(notes), "runs.md")));

            // The real WORK.md on disk.
            var path = Path.Combine(store.AgentRoot, "WORK.md");
            var text = File.ReadAllText(path).TrimStart('﻿');
            string Folder(WorkJob j) => Path.Combine("companions", "openclicky", "jobs", j.Id);
            Assert.StartsWith("# Work\n\n", text);
            var list = text.Split('\n').Where(l => l.StartsWith("- ", StringComparison.Ordinal)).ToList();
            Assert.Equal(
                [$"- Weekly digest — {Folder(digest)} — failed", $"- Book the flight — {Folder(flight)} — needs you", $"- Compare note-taking apps — {Folder(notes)} — done"],
                list);
            var block = text[(text.IndexOf(Open, StringComparison.Ordinal) + Open.Length)..text.IndexOf(Close, StringComparison.Ordinal)]
                .Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(
                [$"{Stamp(T0.AddMinutes(20))} · failed · Couldn't reach the mail page.",
                 $"{Stamp(T0.AddMinutes(10))} · needs you · Waiting for you to sign in.",
                 $"{Stamp(T0)} · done · Compared three note-taking apps and saved a table."],
                block);

            // Text outside the runs block survives the next render, and the block is replaced, not duplicated.
            File.AppendAllText(path, "\n## My notes\nKeep me.\n");
            store.SetJobState(flight.Id, WorkJobStates.Stopped, T0.AddMinutes(30));
            store.WriteIndex();
            var again = File.ReadAllText(path);
            Assert.EndsWith(Close + "\n\n## My notes\nKeep me.\n", again.Replace("\r\n", "\n"));
            Assert.Equal(1, again.Split(Open).Length - 1);
            Assert.Contains($"- Book the flight — {Folder(flight)} — stopped", again);
            Assert.DoesNotContain("needs you", again);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>The card the shell receives for a finished job with a checklist, points, a stat and next steps. Fixed ids, times and path.</summary>
    private static JobCard BuildFinishedCard()
    {
        var folder = @"C:\Users\demo\AppData\Roaming\OpenClicky\companions\openclicky\jobs\note-apps";
        var saved = folder + @"\output\comparison.md";
        var result = new JobResult("Here is the comparison.", "Note-taking comparison", "Compared three note-taking apps and saved a table.",
            ["Obsidian is local-first", "Notion has the best databases", "Apple Notes is the simplest"], new JobStat("3", "apps compared"),
            ["Try Obsidian for a week", "Import your old notes"], [saved], null, null, [])
        { Plan = [new PlanStep("Search for the best apps", true), new PlanStep("Compare features", true), new PlanStep("Save the comparison", true)] };
        var job = new WorkJob("job-notes", WorkStore.DefaultCompanionSlug, "Compare note-taking apps", "Research the three best note-taking apps and save a comparison",
            T0.AddMinutes(-5), WorkJobStates.Completed, "blue", folder, T0, result.Summary!, result.ToJson());
        var inputs = new ShellInputs(new AppSettings(), [new ShellJobInput(job, [], [])], [], [], ShellPhase.Idle, T0) { FeaturedJobId = job.Id };
        var state = ShellStateBuilder.Build(inputs);
        Assert.Equal(state.Featured, Assert.Single(state.Jobs));
        return state.Featured!;
    }

    [Fact]
    public void ShellCheck_FinishedJobCard_MatchesTheFixtureTheShellRenders()
    {
        var card = BuildFinishedCard();
        Assert.Equal((JobState.Done, JobColour.Blue, "3", "apps compared"), (card.State, card.Colour, card.Stat!.Value, card.Stat.Label));
        Assert.Equal(3, card.Plan.Count(s => s.Done));
        Assert.Equal(3, card.Bullets.Count);
        Assert.Equal(2, card.Next.Count);
        Assert.Equal("comparison.md", Assert.Single(card.Files).Name);

        var json = JsonSerializer.Serialize(card, ShellContract.Options);
        var fixture = RepoFile("tests", "fixtures", "shell-job-card.work.v1.json");
        if (Environment.GetEnvironmentVariable("OPENCLICKY_WRITE_FIXTURES") == "1") File.WriteAllText(fixture, json + "\n");
        Assert.Equal(json, File.ReadAllText(fixture).TrimEnd());
    }
}
