using System.Numerics;
using OpenClicky.Core.Drawing;

namespace OpenClicky.Core.Tests;

public sealed class DrawingStoreTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
    // One 200x100 monitor shown as a 100x50 screenshot (scale 2), at virtual (-200, 0): a second monitor to the left.
    private static readonly ScreenCaptureResult[] Screens = [new([0xff, 0xd8], 100, 50, -200, 0, 200, 100, true, "screen 1")];
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "oc-drawings-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (DirectoryNotFoundException) { }
    }

    private static SavedLine Line(string spoken, string reply, params DrawOp[] extra)
    {
        var mapped = DrawingMapper.ToVirtual([.. DrawCommandParser.Parse(reply).Ops, .. extra], Screens);
        return new(spoken, 1.5, mapped.Select(m => m.Virtual).ToArray(), mapped.Select(m => m.Scale).ToArray());
    }

    private static DrawOp FigureOp()
    {
        var figures = FigureBlockParser.Parse("""
            ```figure
            {"id":"gear1","box":[10,5,40,30],"spin":true,"title":"Gear","parts":[
              {"kind":"gear","size":[2,2,0.5],"at":[0,0.25,0],"rotate":[0,45,0],"color":"#E4572E","label":"Driver","teeth":12},
              {"kind":"lathe","size":[1,2,1],"profile":[[0.1,0],[0.5,1],[0.2,2]]}]}
            ```
            """).Figures;
        return FigureBlockParser.ToOp(Assert.Single(figures));
    }

    private static SavedDrawing Drawing(string id, DateTimeOffset at, params SavedLine[] lines) =>
        new(id, "Drew a box", at, lines, [new Box(-200, 0, 200, 100)]);

    private static string Id(int i) => $"d{i:D4}";

    [Fact]
    public void RoundTrip()
    {
        var store = new DrawingStore(_folder);
        var first = Line("Here is the box.", "[DRAW:rect x=10 y=10 w=30 h=20 id=box color=#FF4C4C][DRAW:text x=12 y=12 text=\"Box\" size=18]", FigureOp());
        var second = Line("Now make it bigger.", "[DRAW:scale id=box factor=1.5][DRAW:undo]");
        var saved = Drawing("round-trip_1", T0, first, second);

        Assert.Equal("round-trip_1", store.Save(saved));
        var loaded = store.Load("round-trip_1");

        Assert.NotNull(loaded);
        Assert.Equal(saved.Title, loaded!.Title);
        Assert.Equal(T0, loaded.CreatedAt);
        Assert.Equal(saved.Monitors, loaded.Monitors);
        Assert.Equal(2, loaded.Lines.Count);
        Assert.Equal("Here is the box.", loaded.Lines[0].Spoken);
        Assert.Equal(1.5, loaded.Lines[0].DurationSeconds);
        Assert.Equal(first.Scales, loaded.Lines[0].Scales);
        Assert.Equal(first.Ops.Select(o => o.Kind), loaded.Lines[0].Ops.Select(o => o.Kind));
        Assert.Equal(first.Ops.Select(o => o.Box), loaded.Lines[0].Ops.Select(o => o.Box));
        Assert.Equal(first.Ops[0].Color, loaded.Lines[0].Ops[0].Color);
        Assert.Equal(first.Ops[1].Args, loaded.Lines[0].Ops[1].Args);
        Assert.Equal(first.Ops[1].Text, loaded.Lines[0].Ops[1].Text);
        Assert.Equal(DrawOpKind.Scale, loaded.Lines[1].Ops[0].Kind);
        Assert.True(loaded.Lines[1].Ops[0].IsEdit);

        // The figure keeps its Vector3 fields, parameters, profile and labels.
        var figure = loaded.Lines[0].Ops[2].Figure!;
        var original = first.Ops[2].Figure!;
        Assert.Equal("gear1", figure.Id);
        Assert.True(figure.Spin);
        Assert.Equal("Gear", figure.Title);
        Assert.Equal(original.Box, figure.Box);
        Assert.Equal(2, figure.Parts.Count);
        Assert.Equal(FigureKind.Gear, figure.Parts[0].Kind);
        Assert.Equal(new Vector3(2, 2, 0.5f), figure.Parts[0].Size);
        Assert.Equal(new Vector3(0, 0.25f, 0), figure.Parts[0].At);
        Assert.Equal(new Vector3(0, 45, 0), figure.Parts[0].RotateDegrees);
        Assert.Equal("#E4572E", figure.Parts[0].Color);
        Assert.Equal("Driver", figure.Parts[0].Label);
        Assert.Equal(12, figure.Parts[0].Params["teeth"]);
        Assert.Equal([new Vector2(0.1f, 0), new Vector2(0.5f, 1), new Vector2(0.2f, 2)], figure.Parts[1].Profile);
    }

    [Fact]
    public void SaveWritesNoTempFilesAndReplacesInPlace()
    {
        var store = new DrawingStore(_folder);
        store.Save(Drawing("same", T0, Line("One.", "[DRAW:dot x=5 y=5]")));
        store.Save(Drawing("same", T0.AddMinutes(1), Line("Two.", "[DRAW:dot x=6 y=6]")));
        Assert.Equal(["same.json"], Directory.GetFiles(_folder).Select(Path.GetFileName));
        Assert.Equal("Two.", store.Load("same")!.Lines[0].Spoken);
    }

    [Fact]
    public void PrunesBeyond200()
    {
        var store = new DrawingStore(_folder);
        for (var i = 0; i < 205; i++) store.Save(Drawing(Id(i), T0.AddMinutes(i), Line("Line.", "[DRAW:dot x=5 y=5]")));
        var listed = store.List();
        Assert.Equal(200, listed.Count);
        Assert.Equal(200, Directory.GetFiles(_folder, "*.json").Length);
        for (var i = 0; i < 5; i++) Assert.Null(store.Load(Id(i))); // the oldest five went
        Assert.NotNull(store.Load(Id(5)));
        Assert.Equal(Id(204), listed[0].Id); // newest first
    }

    [Fact]
    public void RejectsOver512KB()
    {
        var store = new DrawingStore(_folder);
        var big = new string('x', 1_900);
        var ops = Enumerable.Range(0, 60).Select(i => new DrawOp(DrawOpKind.Dot, null, [new Pt(5, 5)], new Box(5, 5, 0, 0), null, null, 1,
            new Dictionary<string, string> { ["label"] = big, ["prompt"] = big, ["items"] = big, ["series"] = big, ["title"] = big }, 0)).ToArray();
        var lines = Enumerable.Range(0, 8).Select(_ => new SavedLine("Many.", 1, ops)).ToArray();
        var ex = Assert.Throws<InvalidDataException>(() => store.Save(Drawing("huge", T0, lines)));
        Assert.Contains("too large", ex.Message);
        Assert.False(Directory.Exists(_folder) && Directory.GetFiles(_folder).Length > 0);
    }

    [Fact]
    public void CorruptFileSkippedInList()
    {
        var store = new DrawingStore(_folder);
        store.Save(Drawing("good", T0, Line("Fine.", "[DRAW:dot x=5 y=5]")));
        File.WriteAllText(Path.Combine(_folder, "garbage.json"), "not json at all");
        File.WriteAllText(Path.Combine(_folder, "empty.json"), "");
        File.WriteAllText(Path.Combine(_folder, "truncated.json"), File.ReadAllText(Path.Combine(_folder, "good.json"))[..40]);
        File.WriteAllBytes(Path.Combine(_folder, "huge.json"), new byte[DrawingStore.MaxBytes + 1]);
        File.WriteAllText(Path.Combine(_folder, "other.txt"), "ignored");
        var listed = store.List();
        Assert.Equal(["good"], listed.Select(l => l.Id));
        Assert.Equal("Drew a box", listed[0].Title);
        Assert.Null(store.Load("garbage"));
        Assert.Null(store.Load("huge"));
    }

    [Fact]
    public void DeleteRemoves()
    {
        var store = new DrawingStore(_folder);
        store.Save(Drawing("gone", T0, Line("Bye.", "[DRAW:dot x=5 y=5]")));
        Assert.True(store.Delete("gone"));
        Assert.Null(store.Load("gone"));
        Assert.Empty(store.List());
        Assert.False(store.Delete("gone"));
    }

    [Fact]
    public void ListOfMissingFolderIsEmpty() => Assert.Empty(new DrawingStore(_folder).List());

    [Theory]
    [InlineData(@"..\escape")]
    [InlineData("../escape")]
    [InlineData("a/b")]
    [InlineData("a*")]
    [InlineData("")]
    [InlineData("C:\\x")]
    public void IdsThatAreNotFileNamesAreRefused(string id)
    {
        var store = new DrawingStore(_folder);
        Assert.Null(store.Load(id));
        Assert.False(store.Delete(id));
        Assert.ThrowsAny<Exception>(() => store.Save(Drawing(id, T0, Line("x.", "[DRAW:dot x=5 y=5]"))));
    }

    [Fact]
    public void LoadTreatsTheFileAsUntrusted()
    {
        var store = new DrawingStore(_folder);
        store.Save(Drawing("src", T0, Line("Hello there.", "[DRAW:text x=5 y=5 text=\"Hi\"][DRAW:dot x=5 y=5]")));
        var json = File.ReadAllText(Path.Combine(_folder, "src.json"));
        void Plant(string name, string text) => File.WriteAllText(Path.Combine(_folder, name + ".json"), text);

        Plant("bidi-title", json.Replace("Drew a box", "Drew \\u202Eevil").Replace("\"src\"", "\"bidi-title\""));
        Plant("bidi-spoken", json.Replace("Hello there.", "Hello \\u2067there.").Replace("\"src\"", "\"bidi-spoken\""));
        Plant("control-text", json.Replace("\"Hi\"", "\"H\\ni\"").Replace("\"src\"", "\"control-text\""));
        Plant("huge-number", json.Replace("\"y\":10", "\"y\":9999999").Replace("\"src\"", "\"huge-number\""));
        Plant("wrong-id", json); // the file name says wrong-id, the content says src
        Plant("bad-kind", json.Replace("\"text\"", "\"nonsense\"").Replace("\"src\"", "\"bad-kind\""));
        Plant("bad-colour", json.Replace("\"id\":\"src\"", "\"id\":\"bad-colour\"").Replace("\"kind\":\"dot\"", "\"kind\":\"dot\",\"color\":\"red\""));
        Plant("duration", json.Replace("\"durationSeconds\":1.5", "\"durationSeconds\":9999").Replace("\"src\"", "\"duration\""));

        foreach (var name in new[] { "bidi-title", "bidi-spoken", "control-text", "huge-number", "wrong-id", "bad-kind", "bad-colour", "duration" })
            Assert.True(store.Load(name) is null, name + " was accepted");
        Assert.NotNull(store.Load("src"));
        Assert.Equal(["src"], store.List().Select(l => l.Id));
    }

    [Fact]
    public void LoadRevalidatesAFigureAndItsOps()
    {
        var store = new DrawingStore(_folder);
        store.Save(Drawing("fig", T0, Line("Look.", "[DRAW:dot x=5 y=5]", FigureOp())));
        var json = File.ReadAllText(Path.Combine(_folder, "fig.json"));
        void Plant(string name, string text) => File.WriteAllText(Path.Combine(_folder, name + ".json"), text.Replace("\"id\":\"fig\"", $"\"id\":\"{name}\""));

        Plant("fig-size", json.Replace("\"x\":2,\"y\":2,\"z\":0.5", "\"x\":2000,\"y\":2,\"z\":0.5")); // over the part size limit
        Plant("fig-label", json.Replace("Driver", "Dri\\u202Ever"));
        Plant("fig-teeth", json.Replace("\"teeth\":12", "\"teeth\":1e999"));
        Plant("fig-gone", json.Replace("\"kind\":\"figure\"", "\"kind\":\"dot\"")); // a dot must not carry a figure
        foreach (var name in new[] { "fig-size", "fig-label", "fig-teeth", "fig-gone" }) Assert.True(store.Load(name) is null, name + " was accepted");
        Assert.NotNull(store.Load("fig"));
    }

    [Fact]
    public void ALongestLegalPathSurvivesSaveAndLoad()
    {
        var store = new DrawingStore(_folder);
        var d = "M1 1" + string.Concat(Enumerable.Repeat(" C2 2 3 3 4 4", DrawCommandParser.MaxPathCommands - 1)); // 64 commands, 190 points: over the 32-point cap of the other shapes
        store.Save(Drawing("long-path", T0, Line("A long curve.", $"[DRAW:path d=\"{d}\"]")));

        var path = Assert.Single(store.Load("long-path")!.Lines[0].Ops);
        Assert.Equal(DrawOpKind.Path, path.Kind);
        Assert.Equal(1 + 3 * (DrawCommandParser.MaxPathCommands - 1), path.Points.Count);

        // Still bounded, and only by the length and point limits: 65 commands with a matching 193 points is refused (nothing else is wrong with it).
        var letters = "M" + new string('C', DrawCommandParser.MaxPathCommands);
        var points = Enumerable.Range(0, 1 + 3 * DrawCommandParser.MaxPathCommands).Select(i => new Pt(1 + i % 90, 1 + i % 40)).ToArray();
        Assert.Equal(DrawCommandParser.MaxPathPoints + 1, points.Length);
        DrawOp Hand(string d, Pt[] p) => new(DrawOpKind.Path, null, p, new Box(1, 1, 90, 40), null, null, 1, new Dictionary<string, string> { ["d"] = d }, 0);
        store.Save(Drawing("hand-legal", T0, new SavedLine("Legal.", 1, [Hand(letters[..^1], points[..^3])]))); // same construction, 64 commands: kept
        var tooLong = Hand(letters, points);
        Assert.Throws<ArgumentException>(() => store.Save(Drawing("too-long", T0, new SavedLine("Too long.", 1, [tooLong]))));
    }

    [Fact]
    public void LoadAndListSurviveNullsWhereTheModelHasNone()
    {
        var store = new DrawingStore(_folder);
        store.Save(Drawing("keeper", T0, Line("Fine.", "[DRAW:dot x=5 y=5]")));
        store.Save(Drawing("fig", T0, Line("Look.", "[DRAW:dot x=5 y=5]", FigureOp())));
        var plain = File.ReadAllText(Path.Combine(_folder, "keeper.json"));
        var withFigure = File.ReadAllText(Path.Combine(_folder, "fig.json"));
        void Plant(string name, string text) => File.WriteAllText(Path.Combine(_folder, name + ".json"), text.Replace("\"id\":\"fig\"", $"\"id\":\"{name}\"").Replace("\"id\":\"keeper\"", $"\"id\":\"{name}\""));

        var parts = withFigure[(withFigure.IndexOf("\"parts\":[", StringComparison.Ordinal) + 8)..];
        var partsEnd = parts.IndexOf("]}", StringComparison.Ordinal); // the end of the parts array, before the figure's own close
        Plant("parts-null", withFigure.Replace(withFigure.Substring(withFigure.IndexOf("\"parts\":[", StringComparison.Ordinal), 8 + partsEnd + 1), "\"parts\":null"));
        Plant("parts-null-element", withFigure.Replace("\"parts\":[", "\"parts\":[null,"));
        Plant("lines-null", plain.Replace("\"lines\":[", "\"lines\":null,\"x\":["));
        Plant("ops-null", plain.Replace("\"ops\":[", "\"ops\":null,\"x\":["));
        Plant("op-null-element", plain.Replace("\"ops\":[", "\"ops\":[null,"));
        Plant("args-null", plain.Replace("\"args\":{", "\"args\":null,\"zz\":{"));
        Plant("monitors-null", plain.Replace("\"monitors\":[", "\"monitors\":null,\"x\":["));
        Plant("profile-null-point", withFigure.Replace("\"profile\":[", "\"profile\":[null,"));

        var names = new[] { "parts-null", "parts-null-element", "lines-null", "ops-null", "op-null-element", "args-null", "monitors-null" };
        foreach (var name in names) Assert.True(store.Load(name) is null, name + " was accepted");
        _ = store.Load("profile-null-point"); // a non-fatal answer either way, never an exception
        var listed = store.List(); // no exception, and the good drawings are still there
        Assert.Contains(listed, l => l.Id == "keeper");
        Assert.Contains(listed, l => l.Id == "fig");
    }

    [Fact]
    public void LoadRevalidatesEditAndShapeArguments()
    {
        var store = new DrawingStore(_folder);
        store.Save(Drawing("edits", T0, Line("Draw.", "[DRAW:rect x=5 y=5 w=20 h=10 id=box][DRAW:badge x=9 y=9 n=3][DRAW:text x=6 y=6 text=\"Hi\" size=20][DRAW:path d=\"M 5 5 L 20 20\"]"),
            Line("Edit.", "[DRAW:move id=box dx=10 dy=-4][DRAW:moveto id=box x=7 y=8][DRAW:scale id=box factor=1.5][DRAW:recolor id=box color=#00FF00][DRAW:erase id=box]")));
        var json = File.ReadAllText(Path.Combine(_folder, "edits.json"));
        void Plant(string name, string text) => File.WriteAllText(Path.Combine(_folder, name + ".json"), text.Replace("\"id\":\"edits\"", $"\"id\":\"{name}\""));
        Assert.Contains("\"dx\":\"10\"", json);
        Assert.Contains("\"factor\":\"1.5\"", json);

        var bad = new Dictionary<string, string>
        {
            ["move-no-dx"] = json.Replace("\"dx\":\"10\",", ""),
            ["move-dx-abc"] = json.Replace("\"dx\":\"10\"", "\"dx\":\"abc\""),
            ["move-dy-huge"] = json.Replace("\"dy\":\"-4\"", "\"dy\":\"9999999\""),
            ["moveto-no-y"] = json.Replace("\"y\":\"8\"", "\"z\":\"8\""),
            ["scale-too-big"] = json.Replace("\"factor\":\"1.5\"", "\"factor\":\"9\""),
            ["scale-nan"] = json.Replace("\"factor\":\"1.5\"", "\"factor\":\"NaN\""),
            ["recolor-no-colour"] = json.Replace("\"color\":\"#00FF00\"", "\"color\":null"),
            ["erase-no-id"] = json.Replace("\"id\":\"box\",\"points\":[],\"box\":{\"x\":0,\"y\":0,\"w\":0,\"h\":0}", "\"points\":[],\"box\":{\"x\":0,\"y\":0,\"w\":0,\"h\":0}"),
            ["badge-n-abc"] = json.Replace("\"n\":\"3\"", "\"n\":\"abc\""),
            ["text-size-huge"] = json.Replace("\"size\":\"20\"", "\"size\":\"9999\""),
            ["path-letters"] = json.Replace("\"d\":\"ML\"", "\"d\":\"MLC\""),
        };
        foreach (var (name, text) in bad) Plant(name, text);
        Assert.NotNull(store.Load("edits"));
        foreach (var name in bad.Keys) Assert.True(store.Load(name) is null, name + " was accepted");
    }

    [Fact]
    public void IdsEndExactlyAtTheEnd()
    {
        var store = new DrawingStore(_folder);
        store.Save(Drawing("good", T0, Line("Fine.", "[DRAW:dot x=5 y=5]")));
        Assert.False(DrawingStore.IsValidId("good\n"));
        Assert.Null(store.Load("good\n"));
    }

    [Fact]
    public void SaveTellsInvalidFromTooBig()
    {
        var store = new DrawingStore(_folder);
        var bidi = Drawing("bidi", T0, Line("Hi.", "[DRAW:dot x=5 y=5]")) with { Title = "bad ‮ title" };
        var invalid = Assert.Throws<ArgumentException>(() => store.Save(bidi));
        Assert.Contains("will not keep", invalid.Message);
        var big = new string('x', 1_900);
        var ops = Enumerable.Range(0, 60).Select(_ => new DrawOp(DrawOpKind.Dot, null, [new Pt(5, 5)], new Box(5, 5, 0, 0), null, null, 1,
            new Dictionary<string, string> { ["label"] = big, ["prompt"] = big, ["items"] = big, ["series"] = big, ["title"] = big }, 0)).ToArray();
        var tooBig = Assert.Throws<InvalidDataException>(() => store.Save(Drawing("big", T0, Enumerable.Range(0, 8).Select(_ => new SavedLine("Many.", 1, ops)).ToArray())));
        Assert.Contains("too large", tooBig.Message);
    }

    [Fact]
    public void RecorderLeavesOutOpsTheStoreWouldRefuseAndCapsTheTotal()
    {
        // A MARK label with a bidi character becomes an op the store refuses; the recorded drawing leaves just that op out.
        var marked = DrawCommandParser.FromMarks([new ScreenAnnotation("rect", 5, 5, 20, 10, "tricky ‮ label", 1)]);
        var fine = DrawCommandParser.Parse("[DRAW:dot x=5 y=5]").Ops;
        var recorder = new DrawingTurnRecorder();
        recorder.SetMonitors(Screens);
        recorder.AddLine("Two ops.", TimeSpan.FromSeconds(1), DrawingMapper.ToVirtual([.. marked, .. fine], Screens));
        var built = recorder.Build(T0)!;
        Assert.Equal([DrawOpKind.Dot], built.Lines[0].Ops.Select(o => o.Kind));
        Assert.True(DrawingStore.Validate(built));

        var many = DrawingMapper.ToVirtual(Enumerable.Repeat(fine[0], 60).ToArray(), Screens);
        var capped = new DrawingTurnRecorder();
        capped.SetMonitors(Screens);
        for (var i = 0; i < 12; i++) capped.AddLine("Line " + i, TimeSpan.FromSeconds(1), many);
        var total = capped.Build(T0)!.Lines.Sum(l => l.Ops.Count);
        Assert.Equal(DrawingStore.MaxOps, total);
        Assert.True(DrawingStore.Validate(capped.Build(T0)!));
    }

    [Fact]
    public void RecorderTitlesFromTheFirstSpokenLineAndNeedsAPart()
    {
        var recorder = new DrawingTurnRecorder();
        recorder.SetMonitors(Screens);
        var dot = DrawingMapper.ToVirtual(DrawCommandParser.Parse("[DRAW:dot x=5 y=5]").Ops, Screens);
        var edit = DrawingMapper.ToVirtual(DrawCommandParser.Parse("[DRAW:undo]").Ops, Screens);

        var onlyEdits = new DrawingTurnRecorder();
        onlyEdits.AddLine("Gone again.", TimeSpan.FromSeconds(1), edit);
        Assert.Null(onlyEdits.Build(T0));
        Assert.Null(new DrawingTurnRecorder().Build(T0));

        recorder.AddLine("", TimeSpan.FromSeconds(0.5), []);
        recorder.AddLine("  Look \u202Ehere\n at   this " + new string('z', 100), TimeSpan.FromSeconds(2), dot);
        recorder.AppendToLast(TimeSpan.FromSeconds(1), dot);
        var built = recorder.Build(T0)!;
        Assert.Equal(80, built.Title.Length);
        Assert.StartsWith("Look here at this zzz", built.Title);
        Assert.Equal(32, built.Id.Length);
        Assert.Equal([new Box(-200, 0, 200, 100)], built.Monitors);
        Assert.Equal(2, built.Lines.Count);
        Assert.Equal(2, built.Lines[1].Ops.Count);
        Assert.Equal(2, built.Lines[1].Scales!.Count);
        Assert.True(DrawingStore.Validate(built)); // a recorded turn always passes the store's checks
    }

    [Fact]
    public void RecorderTitleFallsBackAndNeverSplitsASurrogatePair()
    {
        var dot = DrawingMapper.ToVirtual(DrawCommandParser.Parse("[DRAW:dot x=5 y=5]").Ops, Screens);
        var silent = new DrawingTurnRecorder();
        silent.AppendToLast(TimeSpan.FromSeconds(1), dot); // no line yet: a silent one is made
        Assert.Equal("Drawing", silent.Build(T0)!.Title);

        var emoji = new DrawingTurnRecorder();
        emoji.AddLine(new string('a', 79) + "\U0001F600 more", TimeSpan.FromSeconds(1), dot);
        var title = emoji.Build(T0)!.Title;
        Assert.Equal(79, title.Length); // the pair would straddle 80, so it is dropped whole
        Assert.True(DrawingStore.Validate(emoji.Build(T0)!));
    }

    [Fact]
    public void ReplayPlanFitsPartsAndKeepsEditsAndScale()
    {
        // Parts at virtual (-190..-110, 10..50) were drawn at scale 2 from the screenshot (5..45, 5..25).
        var saved = Drawing("plan", T0,
            Line("Draw.", "[DRAW:rect x=5 y=5 w=40 h=20 id=box][DRAW:dot x=45 y=25]"),
            Line("Move it.", "[DRAW:move id=box dx=10 dy=0]"));
        var content = DrawingReplayPlan.ContentBounds(saved)!.Value;
        Assert.Equal(new Box(-190, 10, 80, 40), content);

        // Unfitted: original coordinates, original scale, and a source rebuilt in screenshot pixels.
        var plain = DrawingReplayPlan.Build(saved, null);
        Assert.Equal(new Box(-190, 10, 80, 40), plain[0].Ops[0].Virtual.Box);
        Assert.Equal(new Box(5, 5, 40, 20), plain[0].Ops[0].Source.Box);
        Assert.Equal(2f, plain[0].Ops[0].Scale);
        Assert.True(plain[1].Ops[0].Virtual.IsEdit);

        // Fitted into a box half the size: geometry halves, centred; the scale halves with it. A bigger target never upscales.
        var fitted = DrawingReplayPlan.Build(saved, new Box(1000, 2000, 40, 20));
        var rect = fitted[0].Ops[0].Virtual.Box;
        Assert.Equal(40, rect.W, 3);
        Assert.Equal(20, rect.H, 3);
        Assert.Equal(1000, rect.X, 3);
        Assert.Equal(2000, rect.Y, 3);
        Assert.Equal(1f, fitted[0].Ops[0].Scale, 3);
        Assert.Equal(new Box(5, 5, 40, 20), fitted[0].Ops[0].Source.Box); // the source is what the model saw

        var roomy = DrawingReplayPlan.Build(saved, new Box(0, 0, 4000, 4000));
        Assert.Equal(80, roomy[0].Ops[0].Virtual.Box.W, 3); // a roomy target never upscales...
        Assert.Equal(1960, roomy[0].Ops[0].Virtual.Box.X, 3); // ...it centres: (4000 - 80) / 2
        Assert.Equal(1980, roomy[0].Ops[0].Virtual.Box.Y, 3); // (4000 - 40) / 2
        var upscaled = DrawingReplayPlan.Build(saved, new Box(0, 0, 4000, 4000), maxScale: 10);
        Assert.Equal(800, upscaled[0].Ops[0].Virtual.Box.W, 3);

        // Replay applies the lines to a fresh document; the edit moves the part by dx in source pixels times its scale.
        var document = new DrawingDocument();
        foreach (var line in plain) document.Apply(line.Ops, line.Duration);
        Assert.Equal(-170, document.Parts.Single(p => p.Id == "box").Op.Box.X, 3);
    }

    [Fact]
    public void ReplayPlanTolerantOfMissingScalesAndMonitors()
    {
        var line = Line("Hm.", "[DRAW:dot x=5 y=5]");
        var saved = new SavedDrawing("lonely", "T", T0, [line with { Scales = null }], []);
        var plan = DrawingReplayPlan.Build(saved, null);
        Assert.Equal(1f, plan[0].Ops[0].Scale);
        Assert.True(DrawingStore.Validate(saved));
    }
}
