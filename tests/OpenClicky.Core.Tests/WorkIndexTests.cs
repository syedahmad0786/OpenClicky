using System.Globalization;
using OpenClicky.Core.Work;

namespace OpenClicky.Core.Tests;

public class WorkIndexTests
{
    private const string Open = "<!-- openclicky:runs -->", Close = "<!-- /openclicky:runs -->";
    private static readonly DateTimeOffset T0 = new(2026, 10, 25, 9, 30, 0, TimeSpan.Zero);

    private static string Stamp(DateTimeOffset at) => at.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static string Block(string text)
    {
        var open = text.IndexOf(Open, StringComparison.Ordinal);
        var close = text.IndexOf(Close, StringComparison.Ordinal);
        Assert.True(open >= 0 && close > open, "runs block missing");
        return text[(open + Open.Length)..close];
    }

    [Fact]
    public void RendersJobsNewestFirst()
    {
        var root = Path.Combine(Path.GetTempPath(), "openclicky-index-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new WorkStore(Path.Combine(root, "openclicky.db"), Path.Combine(root, "companions"));
            var older = store.CreateJob(WorkStore.DefaultCompanionSlug, "Older job", "p");
            var newer = store.CreateJob(WorkStore.DefaultCompanionSlug, "Newer job", "p");
            store.SetJobState(older.Id, WorkJobStates.Completed, T0);
            store.WriteIndex();

            var text = File.ReadAllText(Path.Combine(store.RootDirectory, "WORK.md"));
            Assert.StartsWith("# Work\n\n- Newer job — ", text.TrimStart('﻿'));
            Assert.Contains($"- Newer job — {Path.Combine("openclicky", "jobs", newer.Id)} — ready", text);
            Assert.Contains($"- Older job — {Path.Combine("openclicky", "jobs", older.Id)} — done", text);
            Assert.True(text.IndexOf("Newer job", StringComparison.Ordinal) < text.IndexOf("Older job", StringComparison.Ordinal));
            Assert.Equal($"{Stamp(T0)} · done", Block(text).Trim()); // an empty summary leaves no trailing separator
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void StatusWordsForEveryState()
    {
        var states = new[] { "queued", "running", "needs-you", "completed", "failed", "stopped", "ready" };
        var text = WorkIndex.Render("", states.Select(s => new WorkIndexJob("J " + s, "f", s)).ToList(), []);
        Assert.Equal(
            ["- J queued — f — queued", "- J running — f — working", "- J needs-you — f — needs you", "- J completed — f — done",
             "- J failed — f — failed", "- J stopped — f — stopped", "- J ready — f — ready"],
            text.Split('\n').Where(l => l.StartsWith("- ", StringComparison.Ordinal)));
    }

    [Fact]
    public void RunsBlockBetweenMarkers()
    {
        var runs = Enumerable.Range(0, 60).Select(i => new WorkIndexRun(T0.AddMinutes(i), (i % 4) switch
        {
            0 => "completed", 1 => "failed", 2 => "stopped", _ => "running",
        }, "Run " + i)).Reverse().ToList();
        var text = WorkIndex.Render("", [new WorkIndexJob("A", "a", "ready")], runs);

        Assert.Equal($"# Work\n\n- A — a — ready\n\n{Open}\n", text[..(text.IndexOf(Open, StringComparison.Ordinal) + Open.Length + 1)]);
        Assert.EndsWith(Close + "\n", text);
        var lines = Block(text).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(50, lines.Length);
        Assert.Equal($"{Stamp(T0.AddMinutes(59))} · working · Run 59", lines[0]);
        Assert.Equal($"{Stamp(T0.AddMinutes(58))} · stopped · Run 58", lines[1]);
        Assert.Equal($"{Stamp(T0.AddMinutes(57))} · failed · Run 57", lines[2]);
        Assert.Equal($"{Stamp(T0.AddMinutes(56))} · done · Run 56", lines[3]);
        Assert.Equal($"{Stamp(T0.AddMinutes(10))} · stopped · Run 10", lines[^1]);
    }

    [Fact]
    public void PreservesTextOutsideRunsBlock()
    {
        var existing = $"My preface.\n\n# Work\n\n- Old — o — done\n\n{Open}\nold run\n{Close}\n\n## My notes\nKeep me.\n";
        var text = WorkIndex.Render(existing, [new WorkIndexJob("New", "n", "running")], [new WorkIndexRun(T0, "completed", "Fresh")]);

        Assert.StartsWith("My preface.\n\n# Work\n\n- New — n — working\n\n" + Open, text);
        Assert.EndsWith(Close + "\n\n## My notes\nKeep me.\n", text);
        Assert.DoesNotContain("old run", text);
        Assert.DoesNotContain("- Old", text);
        Assert.Contains($"{Stamp(T0)} · done · Fresh", text);
        Assert.Equal(text, WorkIndex.Render(text, [new WorkIndexJob("New", "n", "running")], [new WorkIndexRun(T0, "completed", "Fresh")]));
    }

    [Theory]
    [InlineData("Hand-written notes only.\n")]
    [InlineData("Notes.\n" + Open + "\nhalf a block, no end\n")]
    public void RepairsMissingMarkers(string existing)
    {
        var job = new WorkIndexJob("Job", "j", "completed");
        var run = new WorkIndexRun(T0, "completed", "Done it");
        var text = WorkIndex.Render(existing, [job], [run]);

        Assert.StartsWith(existing.TrimEnd('\n'), text);
        Assert.EndsWith(Close + "\n", text);
        Assert.Contains($"{Stamp(T0)} · done · Done it", Block(text[existing.Length..]));
        // The next render keeps the text, adds the job list before the block and never duplicates the block; after that it is stable.
        var again = WorkIndex.Render(text, [job], [run]);
        Assert.StartsWith(existing.TrimEnd('\n'), again);
        Assert.Equal(2, again.Split(Close).Length);
        Assert.Contains("# Work\n\n- Job — j — done\n\n" + Open, again);
        Assert.Equal(again, WorkIndex.Render(again, [job], [run]));
    }

    [Fact]
    public void SummaryIsFlattenedAndMarkersDefused()
    {
        var text = WorkIndex.Render("", [new WorkIndexJob("Bad\ntitle " + Close, "f", "ready")],
            [new WorkIndexRun(T0, "failed", $"line one\r\nline two {Close} {Open} <!-- x")]);

        Assert.Equal(1, text.Split(Open).Length - 1);
        Assert.Equal(1, text.Split(Close).Length - 1);
        Assert.DoesNotContain("<!-- x", text);
        var lines = Block(text).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith($"{Stamp(T0)} · failed · line one line two ", Assert.Single(lines));
        Assert.Contains("- Bad title ", text);
    }
}
