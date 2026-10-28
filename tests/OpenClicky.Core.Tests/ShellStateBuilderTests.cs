using OpenClicky.Core.Shell;

namespace OpenClicky.Core.Tests;

public class ShellStateBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static WorkJob Job(string id, string status, DateTimeOffset? lastRun = null, string colour = "red") =>
        new(id, "openclicky", "Job " + id, "Do " + id, Now.AddDays(-2), status, colour, @"C:\jobs\" + id, lastRun, "");

    private static ShellInputs Inputs(IReadOnlyList<ShellJobInput>? jobs = null, IReadOnlyList<ConversationTurn>? chat = null) =>
        new(new AppSettings(), jobs ?? [], chat ?? [], [], ShellPhase.Idle, Now);

    [Fact]
    public void CarriesDefaultSettingsAndSection()
    {
        var state = ShellStateBuilder.Build(Inputs());
        Assert.Equal("general", state.Notch.Section);
        Assert.Contains(state.Notch.Section, ShellContract.Sections);
        Assert.Equal("windows-default", state.Settings.Voice.Voice);
        Assert.Equal(1, state.Settings.Voice.Speed);
        Assert.Equal("#3380FF", state.Settings.Cursor.Colour);
        Assert.Equal(HotkeyTrigger.DoubleTap, state.Settings.Shortcuts.HandsFree.Trigger);
        Assert.Empty(state.Settings.Dictation.Dictionary);
    }

    [Fact]
    public void DrawingRowsMergedByTime()
    {
        var chat = new[]
        {
            new ConversationTurn(1, "first q", "first a", Now.AddMinutes(1)),
            new ConversationTurn(2, "second q", "second a", Now.AddMinutes(10)),
        };
        // Supplied newest first and out of order; one lands between the turns, one exactly with the first turn, one after both.
        (string, string, DateTimeOffset)[] drawings =
        [
            ("late", "A late sketch", Now.AddMinutes(20)),
            ("tie", "Tied", Now.AddMinutes(1)),
            ("mid", "Between", Now.AddMinutes(5)),
        ];
        var live = new ChatRow("live-1", ChatRole.User, "typing now", ShellStateBuilder.Iso(Now.AddMinutes(30)));

        var rows = ShellStateBuilder.Build(Inputs(chat: chat) with { Drawings = drawings, LiveRows = [live] }).Chat.Rows;

        Assert.Equal(["first q", "first a", "Drew Tied", "Drew Between", "second q", "second a", "Drew A late sketch", "typing now"], rows.Select(r => r.Text));
        var mid = rows.Single(r => r.DrawingId == "mid");
        Assert.Equal(ChatRole.Event, mid.Role);
        Assert.Equal("drawing-mid", mid.Id);
        Assert.Equal(ShellStateBuilder.Iso(Now.AddMinutes(5)), mid.At);
        Assert.Equal(3, rows.Count(r => r.DrawingId is not null));
        Assert.Null(rows[0].DrawingId);
        Assert.Contains("\"drawingId\":\"mid\"", ShellContract.Serialize(ShellStateBuilder.Build(Inputs(chat: chat) with { Drawings = drawings })));
        Assert.DoesNotContain("drawingId", ShellContract.Serialize(ShellStateBuilder.Build(Inputs(chat: chat))));
    }

    [Fact]
    public void DrawingRowsAreBoundedAndKeepTheNewest()
    {
        var drawings = Enumerable.Range(0, 250).Select(i => ($"d{i}", new string('t', 400), Now.AddMinutes(i))).ToArray();
        var rows = ShellStateBuilder.Build(Inputs() with { Drawings = drawings }).Chat.Rows;
        Assert.Equal(200, rows.Count);
        Assert.Equal("d249", rows[^1].DrawingId);
        Assert.All(rows, r => Assert.True(r.Text.Length <= 205));
    }

    [Fact]
    public void CapsListsAndContent()
    {
        var files = Enumerable.Range(0, 10).Select(i => $@"C:\out\f{i}.md").ToArray();
        var bullets = Enumerable.Range(0, 9).Select(i => "b" + i).ToArray();
        // Supplied oldest first: the builder must reorder by recency before trimming.
        var jobs = Enumerable.Range(0, 100).Reverse().Select(i => new ShellJobInput(Job("j" + i, "completed", Now.AddMinutes(-i)), files, bullets)).ToArray();
        var chat = Enumerable.Range(1, 300).Select(i => new ConversationTurn(i, "u" + i, "a" + i, Now.AddMinutes(i))).ToArray();
        var actions = Enumerable.Range(0, 5).Select(i => new CardAction("a" + i, "Act " + i, CardActionStyle.Secondary)).ToArray();
        var cards = Enumerable.Range(0, 10).Select(i => new NotchCard("c" + i, NotchCardKind.Tip, "Tip", null, actions)).ToArray();
        var document = new DocumentView(@"C:\out\big.md", "big.md", DocumentKind.Markdown, new string('x', 300_000), false);

        var state = ShellStateBuilder.Build(Inputs(jobs, chat) with { Cards = cards, Document = document });

        Assert.Equal(80, state.Jobs.Count);
        Assert.Equal("j0", state.Jobs[0].Id); // newest first
        Assert.Equal("j79", state.Jobs[^1].Id);
        Assert.Equal("c0", state.Cards[0].Id);
        Assert.All(state.Jobs, j => { Assert.Equal(6, j.Files.Count); Assert.Equal(4, j.Bullets.Count); });
        Assert.Equal(200, state.Chat.Rows.Count);
        Assert.Equal("a300", state.Chat.Rows[^1].Text); // newest kept
        Assert.Equal("u201", state.Chat.Rows[0].Text);
        Assert.Equal(6, state.Cards.Count);
        Assert.All(state.Cards, c => Assert.Equal(3, c.Actions.Count));
        Assert.Equal(200_000, state.Document!.Content.Length);
        Assert.True(state.Document.Truncated);
    }

    [Fact]
    public void ChatRowsAreCappedAtEightThousandChars()
    {
        var state = ShellStateBuilder.Build(Inputs(chat: [new ConversationTurn(1, "q", new string('a', 20_000), Now)]));
        var row = state.Chat.Rows.Single(r => r.Role == ChatRole.Assistant);
        Assert.Equal(ShellStateBuilder.MaxRowText, row.Text.Length);
        Assert.EndsWith("…", row.Text);
    }

    [Fact]
    public void SerializedStateStaysWithinBudgetByDroppingOldestRows()
    {
        // 200 rows of ~8,000 chars is ~1.6M chars before the budget applies.
        var chat = Enumerable.Range(1, 100).Select(i => new ConversationTurn(i, $"u{i} " + new string('u', 9_000), $"a{i} " + new string('a', 9_000), Now)).ToArray();
        var state = ShellStateBuilder.Build(Inputs(chat: chat));
        Assert.True(ShellContract.Serialize(state).Length <= ShellStateBuilder.MaxStateChars);
        Assert.InRange(state.Chat.Rows.Count, 100, 199);
        Assert.StartsWith("a100 ", state.Chat.Rows[^1].Text); // newest kept
    }

    [Theory]
    [InlineData("running", false, JobState.Working)]
    [InlineData("completed", false, JobState.Done)]
    [InlineData("failed", false, JobState.Failed)]
    [InlineData("stopped", false, JobState.Stopped)]
    [InlineData("ready", true, JobState.Queued)]
    [InlineData("ready", false, JobState.New)]
    [InlineData("queued", false, JobState.Queued)]
    [InlineData("needs-you", false, JobState.NeedsYou)]
    public void MapsJobStates(string status, bool dueRoutine, JobState expected)
    {
        var state = ShellStateBuilder.Build(Inputs([new ShellJobInput(Job("x", status, colour: "yellow"), [], [], HasDueRoutine: dueRoutine)]));
        Assert.Equal(expected, state.Jobs.Single().State);
        Assert.Equal(JobColour.Yellow, state.Jobs.Single().Colour);
    }

    [Fact]
    public void UnknownColourFallsBackToTheIdHash()
    {
        var card = ShellStateBuilder.Build(Inputs([new ShellJobInput(Job("abc", "ready", colour: "purple"), [], [])])).Jobs.Single();
        Assert.Equal(Work.JobColours.FromId("abc"), card.Colour.ToString().ToLowerInvariant());
    }

    [Fact]
    public void ColoursAreStableAndFilesTyped()
    {
        var input = new ShellJobInput(Job("abc", "completed", Now), [@"C:\o\report.pdf", @"C:\o\data.csv", @"C:\o\pic.PNG", @"C:\o\x.bin"], []);
        var first = ShellStateBuilder.Build(Inputs([input])).Jobs.Single();
        var second = ShellStateBuilder.Build(Inputs([input])).Jobs.Single();
        Assert.Equal(first.Colour, second.Colour);
        Assert.Equal([JobFileKind.Pdf, JobFileKind.Sheet, JobFileKind.Image, JobFileKind.Other], first.Files.Select(f => f.Kind));
        Assert.Equal("report.pdf", first.Files[0].Name);
    }

    [Fact]
    public void FeaturesMostRecentlyRunJobWithOutputsUnlessChosen()
    {
        var older = new ShellJobInput(Job("old", "completed", Now.AddHours(-3)), [@"C:\o\a.md"], []);
        var newer = new ShellJobInput(Job("new", "completed", Now.AddHours(-1)), [@"C:\o\b.md"], []);
        var noFiles = new ShellJobInput(Job("empty", "completed", Now), [], []);
        Assert.Equal("new", ShellStateBuilder.Build(Inputs([older, newer, noFiles])).Featured!.Id);
        Assert.Equal("old", ShellStateBuilder.Build(Inputs([older, newer, noFiles]) with { FeaturedJobId = "old" }).Featured!.Id);
        Assert.Equal("old", ShellStateBuilder.Build(Inputs([older, newer]) with { DismissedJobIds = new HashSet<string> { "new" } }).Featured!.Id);
        Assert.Null(ShellStateBuilder.Build(Inputs([noFiles])).Featured);
    }

    [Fact]
    public void CardShowsTheStoredResult()
    {
        var folder = Path.Combine(Path.GetTempPath(), "openclicky-card-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "output"));
        try
        {
            File.WriteAllText(Path.Combine(folder, "output", "kept.md"), "x");
            File.WriteAllText(Path.Combine(folder, "output", "gone.md"), "x");
            var result = Work.JobResult.Parse("Done.\n```openclicky-result\n" + """
                {"title":"Model title","points":["Found it"],"stat":{"value":"3","label":"files"},"next":["Share it","Print it"],
                 "files":["output/kept.md","output/gone.md"]}
                """ + "\n```", folder);
            var job = Job("r", "completed", Now) with { FolderPath = folder, ResultJson = result.ToJson() };
            var state = ShellStateBuilder.Build(Inputs([new ShellJobInput(job, [@"C:\o\listing.md"], ["Live step"])]));
            var card = state.Jobs.Single();
            Assert.Equal("Job r", card.Title);
            Assert.Equal(["Found it"], card.Bullets);
            Assert.Equal(new JobStat("3", "files"), card.Stat);
            Assert.Equal(["Share it", "Print it"], card.Next);
            Assert.Equal([Path.Combine(folder, "output", "kept.md"), Path.Combine(folder, "output", "gone.md")], card.Files.Select(f => f.Path));
            Assert.Equal("r", state.Featured!.Id);

            var noFiles = job with { ResultJson = Work.JobResult.Parse("Done.", folder).ToJson() };
            Assert.Equal([@"C:\o\listing.md"], ShellStateBuilder.Build(Inputs([new ShellJobInput(noFiles, [@"C:\o\listing.md"], [])])).Jobs.Single().Files.Select(f => f.Path));

            var running = ShellStateBuilder.Build(Inputs([new ShellJobInput(job with { Status = "running" }, [], ["Live step"])])).Jobs.Single();
            Assert.Equal(["Live step"], running.Bullets); // a new run shows its own steps, not the last run's points
            Assert.Empty(ShellStateBuilder.Build(Inputs([new ShellJobInput(Job("plain", "completed", Now), [], [])])).Jobs.Single().Next);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void PlanComesFromTheLiveRunWhileWorkingAndFromTheStoredResultAfter()
    {
        PlanStep[] live = [new("Find it", true), new("Write it", false)];
        var stored = (Work.JobResult.Parse("Done.", "") with { Plan = [new PlanStep("Old step", true)] }).ToJson();
        var job = Job("p", "running", Now) with { ResultJson = stored };

        var running = ShellStateBuilder.Build(Inputs([new ShellJobInput(job, [], ["Clicking"], "Clicking", Plan: live)])).Jobs.Single();
        Assert.Equal(live, running.Plan);
        Assert.Equal("Clicking", running.LiveLine);
        Assert.Equal("Waiting for a free slot", ShellStateBuilder.Build(Inputs([new ShellJobInput(job with { Status = "queued" }, [], [], Work.JobAdmission.WaitingLine)])).Jobs.Single().LiveLine);
        Assert.Null(ShellStateBuilder.Build(Inputs([new ShellJobInput(job with { Status = "completed" }, [], [], "Clicking")])).Jobs.Single().LiveLine);

        var finished = ShellStateBuilder.Build(Inputs([new ShellJobInput(job with { Status = "completed" }, [], [], Plan: live)])).Jobs.Single();
        Assert.Equal([new PlanStep("Old step", true)], finished.Plan);

        var many = Enumerable.Range(0, 40).Select(i => new PlanStep("Step " + i, false)).ToList();
        Assert.Equal(24, ShellStateBuilder.Build(Inputs([new ShellJobInput(job, [], [], Plan: many)])).Jobs.Single().Plan.Count);
        Assert.Empty(ShellStateBuilder.Build(Inputs([new ShellJobInput(Job("q", "running"), [], [])])).Jobs.Single().Plan);
    }

    [Fact]
    public void DisplayNameAccentAndStatus()
    {
        var settings = new AppSettings { ChatGptAccountEmail = "ahmad.b@example.com", AccentColor = "not a colour" };
        var state = ShellStateBuilder.Build(new ShellInputs(settings, [new ShellJobInput(Job("r", "running"), [], [])], [], [], ShellPhase.Working, Now) { FallbackUserName = "win" });
        Assert.Equal("Ahmad", state.User.DisplayName);
        Assert.Equal("#3380FF", state.Accent);
        Assert.Equal(1, state.Status.JobsWorking);
        Assert.Equal(["Ctrl", "Alt"], state.Hotkeys.Talk);
        Assert.Equal(["Ctrl"], state.Hotkeys.Text);

        settings.DisplayName = "Sam";
        Assert.Equal("Sam", ShellStateBuilder.Build(new ShellInputs(settings, [], [], [], ShellPhase.Idle, Now)).User.DisplayName);
        Assert.Equal("win", ShellStateBuilder.Build(new ShellInputs(new AppSettings(), [], [], [], ShellPhase.Idle, Now) { FallbackUserName = "win" }).User.DisplayName);
    }

    [Theory]
    [InlineData("Sam", "Ahmad Bukhari", "ahmad@x.com", "win", "Sam")]                    // saved name wins
    [InlineData("", "Ahmad Bukhari", "ahmadbukhari4245@gmail.com", "win", "Ahmad")]      // Windows account: first word
    [InlineData("", "  ", "ahmadbukhari4245@gmail.com", "win", "Ahmadbukhari")]          // email: stop at the first digit
    [InlineData("", "", "ahmad.bukhari@x.com", "win", "Ahmad")]                          // email: stop at '.'
    [InlineData("", "", "ahmad_b@x.com", "win", "Ahmad")]                                // email: stop at '_'
    [InlineData("", "", "ahmad-b@x.com", "win", "Ahmad")]                                // email: stop at '-'
    [InlineData("", "", "4245@x.com", "win", "win")]                                     // no leading letters
    [InlineData("", "", null, "win", "win")]                                             // nothing else: OS user
    public void DisplayNameChain(string saved, string account, string? email, string user, string expected)
    {
        var settings = new AppSettings { DisplayName = saved, ChatGptAccountEmail = email };
        var inputs = new ShellInputs(settings, [], [], [], ShellPhase.Idle, Now) { AccountDisplayName = account, FallbackUserName = user };
        Assert.Equal(expected, ShellStateBuilder.Build(inputs).User.DisplayName);
    }

    [Fact]
    public void BuiltStateMatchesTheContract()
    {
        var json = ShellContract.Serialize(ShellStateBuilder.Build(Inputs([new ShellJobInput(Job("x", "completed", Now), [@"C:\a.md"], ["one"])],
            [new ConversationTurn(1, "hi", "hello", Now)])));
        Assert.Contains("\"kind\":\"doc\"", json);
        Assert.Contains("\"at\":\"2026-10-24T12:00:00.000Z\"", json);
        Assert.NotNull(ShellContract.Deserialize(json));
    }

    [Fact]
    public void BoundsFilesUpdateAndEmptyIds()
    {
        var longPath = @"C:\" + new string('p', 5_000) + ".md";
        var jobs = new[]
        {
            new ShellJobInput(Job("keep", "completed", Now), [longPath, @"C:\ok.md"], []),
            new ShellJobInput(Job("", "completed", Now), [@"C:\ghost.md"], []),
        };
        var inputs = new ShellInputs(new AppSettings(), jobs, [], [new ConnectorInfo("", "No key", "h", Now), new ConnectorInfo("gh", "GitHub", "api.github.com", Now)],
            ShellPhase.Idle, Now)
        {
            Update = new ShellUpdate(new string('1', 90), new string('2', 90), UpdateState.Available, 7),
        };
        var state = ShellStateBuilder.Build(inputs);

        var job = Assert.Single(state.Jobs);
        Assert.Equal("keep", job.Id);
        Assert.Equal(@"C:\ok.md", Assert.Single(job.Files).Path); // the over-long path is dropped, never cut
        Assert.Equal("gh", Assert.Single(state.Apps.Connected).Id);
        Assert.Equal(40, state.Update.Current.Length);
        Assert.Equal(40, state.Update.Available!.Length);
        Assert.Equal(1, state.Update.Progress);

        var json = ShellContract.Serialize(state);
        Assert.Equal(json, ShellContract.Serialize(ShellContract.Deserialize(json)!));
    }
}
