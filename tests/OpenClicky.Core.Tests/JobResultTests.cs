using OpenClicky.Core.Shell;
using OpenClicky.Core.Work;

namespace OpenClicky.Core.Tests;

public class JobResultTests
{
    private const string Fence = "```";

    private static string Block(string json) => $"{Fence}{JobResult.FenceTag}\n{json}\n{Fence}";

    private static void InJobFolder(Action<string> test)
    {
        var root = Path.Combine(Path.GetTempPath(), "openclicky-result-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "output"));
        try { test(root); }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ParsesAllFields() => InJobFolder(folder =>
    {
        File.WriteAllText(Path.Combine(folder, "output", "report.pdf"), "x");
        var reply = "Saved the report.\n\n" + Block("""
            {"title":"Q3 report","summary":"Revenue rose.","points":["Up 4%","Costs flat"],"stat":{"value":"4%","label":"growth"},
             "next":["Share it"],"files":["output/report.pdf"],"routine":{"title":"Weekly report","task":"Redo it","every_minutes":60},
             "your_turn":{"step":"Sign in","page":"https://example.com/login","done_when_url_contains":"/home"},"learned":["Excel opens slowly"],
             "unknown":{"ignored":true}}
            """);
        var result = JobResult.Parse(reply, folder);
        Assert.Equal("Saved the report.", result.Reply);
        Assert.Equal("Q3 report", result.Title);
        Assert.Equal("Revenue rose.", result.Summary);
        Assert.Equal(["Up 4%", "Costs flat"], result.Points);
        Assert.Equal(new JobStat("4%", "growth"), result.Stat);
        Assert.Equal(["Share it"], result.Next);
        Assert.Equal([Path.Combine(folder, "output", "report.pdf")], result.Files);
        Assert.Equal(new RoutineRequest("Weekly report", "Redo it", 60, null, 0), result.Routine);
        Assert.Equal("Sign in", result.YourTurn!.Step);
        Assert.Equal("https://example.com/login", result.YourTurn.Page);
        Assert.Equal("/home", result.YourTurn.DoneWhenUrlContains);
        Assert.True(DateTimeOffset.UtcNow - result.YourTurn.AskedAt < TimeSpan.FromMinutes(1));
        Assert.Equal(["Excel opens slowly"], result.Learned);
        Assert.Empty(result.Plan);
    });

    [Fact]
    public void StripsBlockFromReply()
    {
        var result = JobResult.Parse("Before.\n" + Block("""{"title":"T"}""") + "\nAfter.", "");
        Assert.Equal("Before.\n\nAfter.", result.Reply);
        Assert.DoesNotContain(JobResult.FenceTag, result.Reply);
        Assert.Equal("T", result.Title);
        var crlf = JobResult.Parse("Done.\r\n```openclicky-result\r\n{\"title\":\"T\"}\r\n```\r\n", "");
        Assert.Equal(("Done.", "T"), (crlf.Reply, crlf.Title));
    }

    [Theory]
    [InlineData("```OpenClicky-Result\n{\"title\":\"T\"}\n```", "T")]
    [InlineData("```openclicky-result json\n{\"title\":\"T\"}\n```", "T")]
    [InlineData("```openclicky-resultjson\n{\"title\":\"T\"}\n```", null)]
    public void FenceTagIsCaseInsensitiveAndMayHaveMoreWords(string block, string? title)
    {
        var result = JobResult.Parse("Done.\n" + block, "");
        Assert.Equal(title, result.Title);
        Assert.Equal(title is null ? "Done.\n" + block : "Done.", result.Reply);
    }

    [Theory]
    [InlineData("""{"value":12,"label":"photos"}""", "12")]
    [InlineData("""{"value":4.5,"label":"photos"}""", "4.5")]
    [InlineData("""{"value":"12%","label":"photos"}""", "12%")]
    public void StatValueMayBeANumber(string stat, string value) =>
        Assert.Equal(new JobStat(value, "photos"), JobResult.Parse(Block("{\"stat\":" + stat + "}"), "").Stat);

    [Fact]
    public void LastBlockWins()
    {
        var result = JobResult.Parse("Done.\n" + Block("""{"title":"First"}""") + "\n" + Block("""{"title":"Second"}"""), "");
        Assert.Equal("Second", result.Title);
        Assert.Equal("Done.", result.Reply);
    }

    [Theory]
    [InlineData("""{"title":""")]
    [InlineData("""["not","an","object"]""")]
    public void MalformedJsonKeepsReply(string json)
    {
        var reply = "Done.\n" + Block(json);
        var result = JobResult.Parse(reply, "");
        Assert.Equal(reply, result.Reply);
        Assert.Null(result.Title);
        Assert.Null(result.Summary);
        Assert.Empty(result.Points);
        Assert.Null(result.Stat);
        Assert.Empty(result.Next);
        Assert.Empty(result.Files);
        Assert.Null(result.Routine);
        Assert.Null(result.YourTurn);
        Assert.Empty(result.Learned);
    }

    [Fact]
    public void CapsLists() => InJobFolder(folder =>
    {
        var names = Enumerable.Range(0, 8).Select(i => $"output/f{i}.txt").ToArray();
        foreach (var name in names) File.WriteAllText(Path.Combine(folder, name), "x");
        string Long(int n) => new('a', n);
        string Many(int count, int length) => "[" + string.Join(",", Enumerable.Repeat($"\"{Long(length)}\"", count)) + "]";
        var json = $$$"""
            {"title":"{{{Long(500)}}}","summary":"{{{Long(500)}}}","points":{{{Many(6, 300)}}},"next":{{{Many(5, 300)}}},
             "files":[{{{string.Join(",", names.Select(n => $"\"{n}\""))}}}],"learned":{{{Many(7, 300)}}},
             "your_turn":{"step":"{{{Long(700)}}}","page":"https://example.com/{{{Long(1000)}}}","done_when_url_contains":"{{{Long(300)}}}"}}
            """;
        var result = JobResult.Parse(Block(json), folder);
        Assert.Equal(120, result.Title!.Length);
        Assert.Equal(300, result.Summary!.Length);
        Assert.Equal(4, result.Points.Count);
        Assert.All(result.Points, p => Assert.Equal(200, p.Length));
        Assert.Equal(3, result.Next.Count);
        Assert.All(result.Next, p => Assert.Equal(160, p.Length));
        Assert.Equal(6, result.Files.Count);
        Assert.Equal(5, result.Learned.Count);
        Assert.All(result.Learned, p => Assert.Equal(240, p.Length));
        Assert.Equal(600, result.YourTurn!.Step.Length);
        Assert.Equal(200, result.YourTurn.DoneWhenUrlContains!.Length);
        Assert.Equal("https://example.com/" + Long(1000), result.YourTurn.Page);

        // A link is never cut (it would point somewhere else): one over 2048 characters is dropped.
        var tooLong = JobResult.Parse(Block($$$"""{"your_turn":{"step":"Go","page":"https://example.com/{{{Long(2100)}}}"}}"""), folder);
        Assert.Null(tooLong.YourTurn!.Page);
    });

    [Fact]
    public void RejectsPathsOutsideJobFolder() => InJobFolder(folder =>
    {
        File.WriteAllText(Path.Combine(folder, "output", "report.pdf"), "x");
        var outside = Path.Combine(Path.GetTempPath(), "openclicky-outside-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(outside, "x");
        var sibling = folder + "-sibling";
        Directory.CreateDirectory(sibling);
        File.WriteAllText(Path.Combine(sibling, "x.txt"), "x");
        try
        {
            var files = new[] { @"..\x", outside, "output/missing.pdf", "../" + Path.GetFileName(sibling) + "/x.txt", "output/report.pdf:hidden", "output/report.pdf" };
            var json = "{\"files\":[" + string.Join(",", files.Select(f => System.Text.Json.JsonSerializer.Serialize(f))) + "]}";
            var result = JobResult.Parse(Block(json), folder);
            Assert.Equal([Path.Combine(folder, "output", "report.pdf")], result.Files);
            Assert.Empty(JobResult.Parse(Block(json), "").Files);
        }
        finally
        {
            File.Delete(outside);
            Directory.Delete(sibling, recursive: true);
        }
    });

    [Theory]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("file:///C:/Windows/win.ini", null)]
    [InlineData("data:text/html,hi", null)]
    [InlineData("example.com/login", null)]
    [InlineData("https://example.com/login", "https://example.com/login")]
    [InlineData("http://example.com/", "http://example.com/")]
    [InlineData("HTTPS://Example.COM/a b", "https://example.com/a%20b")]
    [InlineData("https://example.com", "https://example.com/")]
    public void RejectsNonHttpPage(string page, string? expected)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new { your_turn = new { step = "Sign in", page } });
        Assert.Equal(expected, JobResult.Parse(Block(json), "").YourTurn!.Page);
    }

    [Theory]
    [InlineData("""{"every_minutes":60}""", 60, null, 0)]
    [InlineData("""{"at":"08:30","days":["mon","WED"]}""", null, 510, (1 << (int)DayOfWeek.Monday) | (1 << (int)DayOfWeek.Wednesday))]
    [InlineData("""{"at":"23:59"}""", null, 1439, 127)]
    public void RoutineCadence(string cadence, int? every, int? at, int mask)
    {
        var json = cadence.Insert(1, "\"title\":\"Report\",\"task\":\"Make it\",");
        var routine = JobResult.Parse(Block("{\"routine\":" + json + "}"), "").Routine;
        Assert.Equal(new RoutineRequest("Report", "Make it", every, at, mask), routine);
    }

    [Theory]
    [InlineData("""{"title":"R","task":"t","every_minutes":4}""")]
    [InlineData("""{"title":"R","task":"t","every_minutes":10081}""")]
    [InlineData("""{"title":"R","task":"t","at":"24:00"}""")]
    [InlineData("""{"title":"R","task":"t","at":"8.30"}""")]
    [InlineData("""{"title":"R","task":"t","at":"08:30","days":["someday"]}""")]
    [InlineData("""{"title":"R","task":"t"}""")]
    [InlineData("""{"title":"","task":"t","every_minutes":60}""")]
    public void RoutineCadenceRejectsInvalid(string routine) =>
        Assert.Null(JobResult.Parse(Block("{\"routine\":" + routine + "}"), "").Routine);

    [Fact]
    public void RoundTripsJson() => InJobFolder(folder =>
    {
        File.WriteAllText(Path.Combine(folder, "output", "a.md"), "x");
        var parsed = JobResult.Parse("Done.\n" + Block("""
            {"title":"T","summary":"S","points":["p"],"stat":{"value":"1","label":"l"},"next":["n"],"files":["output/a.md"],
             "routine":{"title":"R","task":"t","at":"07:00","days":["sun"]},"your_turn":{"step":"s","page":"https://x.test/"},"learned":["l1"]}
            """), folder) with { Plan = [new PlanStep("Open it", true)] };
        var again = JobResult.FromJson(parsed.ToJson());
        Assert.NotNull(again);
        Assert.Equal(parsed.ToJson(), again.ToJson());
        Assert.Equal(parsed.YourTurn, again.YourTurn);
        Assert.Equal(parsed.Plan, again.Plan);
        Assert.Equal(("T", 1, 1), (again.Title, again.Files.Count, again.Learned.Count));
        Assert.Contains("\"askedAt\"", parsed.ToJson()); // WorkStore.RecoverInterrupted reads this name
        Assert.Null(JobResult.FromJson(""));
        Assert.Null(JobResult.FromJson("{broken"));
        Assert.Empty(JobResult.FromJson("{\"reply\":\"x\"}")!.Points);
    });

    [Fact]
    public void AStoredResultLeavesTheReplyOut()
    {
        var produced = JobResult.Parse("A very long reply.\n" + Block("""{"summary":"S","points":["p"]}"""), "");
        var stored = JobResult.ForStore(produced, previous: null, plan: [])!;
        Assert.Equal("", stored.Reply);
        Assert.Equal(["p"], stored.Points);
        Assert.Equal("", JobResult.FromJson(stored.ToJson())!.Reply);
    }

    [Fact]
    public void ARunWithoutAResultKeepsThePreviousOneAndItsOwnPlan()
    {
        var previous = JobResult.Parse("Old reply\n" + Block("""{"points":["kept"],"stat":{"value":"4","label":"rows"},"next":["n"]}"""), "")
            with { Plan = [new PlanStep("Old", true)] };
        var plan = new[] { new PlanStep("New", false) };
        var kept = JobResult.ForStore(produced: null, previous, plan)!;
        Assert.Equal(["kept"], kept.Points);
        Assert.Equal(new JobStat("4", "rows"), kept.Stat);
        Assert.Equal(["n"], kept.Next);
        Assert.Equal(plan, kept.Plan);
        Assert.Equal("", kept.Reply);
        Assert.Equal(previous.Plan, JobResult.ForStore(null, previous, [])!.Plan); // no new checklist: the old one stays
        Assert.Equal(plan, JobResult.ForStore(null, null, plan)!.Plan);
        Assert.Null(JobResult.ForStore(null, null, []));
        var fresh = JobResult.Parse(Block("""{"points":["new"]}"""), "");
        Assert.Equal(["new"], JobResult.ForStore(fresh, previous, plan)!.Points); // a result of its own replaces the old one
    }
}
