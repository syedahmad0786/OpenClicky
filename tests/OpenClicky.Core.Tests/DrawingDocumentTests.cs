using OpenClicky.Core.Drawing;

namespace OpenClicky.Core.Tests;

public class DrawingDocumentTests
{
    // A 1000x500 screenshot of a 2000x1000 monitor at the origin: virtual = 2 x screenshot.
    private static readonly ScreenCaptureResult[] Screens = [new([0xff, 0xd8], 1000, 500, 0, 0, 2000, 1000, true, "screen 1")];

    private static IReadOnlyList<PartAnimation> Apply(DrawingDocument doc, string reply, double lineSeconds = 2) =>
        doc.Apply(DrawingMapper.ToVirtual(DrawCommandParser.Parse(reply).Ops, Screens), TimeSpan.FromSeconds(lineSeconds));

    private static string Dots(int count, int from = 0) =>
        string.Concat(Enumerable.Range(from, count).Select(i => $"[DRAW:dot x={i} y=1]"));

    [Fact]
    public void SequentialRevealTiming()
    {
        var doc = new DrawingDocument();
        var animations = Apply(doc, Dots(4), lineSeconds: 2);
        Assert.Equal([0, 0.5, 1, 1.5], animations.Select(a => a.Start.TotalSeconds));
        Assert.All(animations, a => { Assert.Equal(0.5, a.Duration.TotalSeconds); Assert.False(a.Erase); });
        Assert.Equal(["p1", "p2", "p3", "p4"], animations.Select(a => a.Part.Id));
        Assert.Equal(4, doc.Parts.Count);
        Assert.False(doc.IsEmpty);
    }

    [Fact]
    public void RevealClampedTo250msAnd1200ms()
    {
        Assert.Equal(1.2, Assert.Single(Apply(new DrawingDocument(), Dots(1), lineSeconds: 5)).Duration.TotalSeconds);
        var fast = Apply(new DrawingDocument(), Dots(8), lineSeconds: 1);
        Assert.All(fast, a => Assert.Equal(0.25, a.Duration.TotalSeconds));
        Assert.Equal(1.75, fast[^1].Start.TotalSeconds);
    }

    [Fact]
    public void LineDurationHasFloorAndFollowsSpeed()
    {
        Assert.Equal(0.6, DrawingPacing.LineDuration("Hi.", 1).TotalSeconds);
        Assert.Equal(6.5, DrawingPacing.LineDuration(new string('a', 200), 2).TotalSeconds, 6);
        // A bad speed setting falls back to normal speed instead of dividing by zero.
        Assert.Equal(13, DrawingPacing.LineDuration(new string('a', 200), 0).TotalSeconds, 6);
    }

    [Fact]
    public void SameIdReplaces()
    {
        var doc = new DrawingDocument();
        Apply(doc, "[DRAW:rect id=a x=1 y=1 w=5 h=5][DRAW:dot id=b x=3 y=3]");
        var animations = Apply(doc, "[DRAW:dot id=a x=9 y=9]");
        Assert.Equal(["a", "b"], doc.Parts.Select(p => p.Id));
        Assert.Equal(DrawOpKind.Dot, doc.Parts[0].Op.Kind);
        Assert.Same(doc.Parts[0], Assert.Single(animations).Part);
    }

    [Fact]
    public void MaxParts64()
    {
        var doc = new DrawingDocument();
        for (var batch = 0; batch < 3; batch++) Apply(doc, Dots(24, batch * 24));
        Assert.Equal(DrawingDocument.MaxParts, doc.Parts.Count);
        Assert.Equal(63, doc.Parts[^1].Source.Points[0].X);
        // Replacing an existing id still works when full.
        Apply(doc, "[DRAW:ring id=p1 x=50 y=50 r=5]");
        Assert.Equal(DrawOpKind.Ring, doc.Parts[0].Op.Kind);
    }

    [Fact]
    public void MoveUsesPartScale()
    {
        var doc = new DrawingDocument();
        Apply(doc, "[DRAW:rect id=a x=10 y=10 w=4 h=4]");
        Assert.Empty(Apply(doc, "[DRAW:move id=a dx=5 dy=-3]"));
        var part = Assert.Single(doc.Parts);
        Assert.Equal(new Box(30, 14, 8, 8), part.Op.Box);
        Assert.Equal(new Box(15, 7, 4, 4), part.Source.Box);

        Apply(doc, "[DRAW:moveto id=a x=100 y=200][DRAW:recolor id=a color=#FF4C4C][DRAW:scale id=a factor=2]");
        part = Assert.Single(doc.Parts);
        Assert.Equal(new Box(98, 198, 8, 8), part.Source.Box);
        Assert.Equal(new Box(196, 396, 16, 16), part.Op.Box);
        Assert.Equal("#FF4C4C", part.Op.Color);
        Assert.Equal("#FF4C4C", part.Source.Color);
    }

    [Fact]
    public void EraseReturnsReverseAnimation()
    {
        var doc = new DrawingDocument();
        Apply(doc, "[DRAW:dot id=a x=1 y=1]");
        var erase = Assert.Single(Apply(doc, "[DRAW:erase id=a][DRAW:erase id=missing]"));
        Assert.True(erase.Erase);
        Assert.Equal("a", erase.Part.Id);
        Assert.Equal(TimeSpan.Zero, erase.Start);
        Assert.Equal(0.4, erase.Duration.TotalSeconds);
        Assert.True(doc.IsEmpty);
        // A part drawn and erased in the same reply was never shown, so nothing animates.
        Assert.Empty(Apply(doc, "[DRAW:dot id=b x=1 y=1][DRAW:erase id=b]"));
    }

    [Fact]
    public void UndoRestoresPrevious()
    {
        var doc = new DrawingDocument();
        Apply(doc, "[DRAW:dot id=a x=1 y=1][DRAW:dot id=b x=2 y=2]");
        Apply(doc, "[DRAW:undo]");
        Assert.Equal(["a"], doc.Parts.Select(p => p.Id));
        Apply(doc, "[DRAW:move id=a dx=10 dy=0][DRAW:undo]");
        Assert.Equal(1, doc.Parts[0].Source.Points[0].X);
        Apply(doc, "[DRAW:clear]");
        Assert.True(doc.IsEmpty);
        Apply(doc, "[DRAW:undo]");
        Assert.Equal(["a"], doc.Parts.Select(p => p.Id));

        // The stack keeps 20 snapshots.
        var deep = new DrawingDocument();
        Apply(deep, Dots(24));
        Apply(deep, string.Concat(Enumerable.Repeat("[DRAW:undo]", 24)));
        Assert.Equal(4, deep.Parts.Count);

        deep.Clear();
        Assert.True(deep.IsEmpty);
        Apply(deep, "[DRAW:undo]");
        Assert.True(deep.IsEmpty);
    }

    [Fact]
    public void DescribeUsesScreenshotCoordinatesAndQuotesLabels()
    {
        var doc = new DrawingDocument();
        Apply(doc, """[DRAW:rect id=save x=10 y=20 w=30 h=40 label="Save \"now\""][DRAW:dot x=5 y=6]""");
        Assert.Equal("save rect \"Save \\\"now\\\"\" at 10,20 30×40 screen1\np2 dot at 5,6 0×0 screen1", doc.Describe());
        Assert.Equal("", new DrawingDocument().Describe());
    }

    [Fact]
    public void DescribeCappedAt2000()
    {
        var doc = new DrawingDocument();
        var label = new string('x', DrawCommandParser.MaxLabel);
        for (var batch = 0; batch < 3; batch++)
            Apply(doc, string.Concat(Enumerable.Range(batch * 24, 24).Select(i => $"[DRAW:point x={i} y=1 label={label}]")));
        var text = doc.Describe();
        Assert.InRange(text.Length, 1900, 2000);
        Assert.All(text.Split('\n'), line => Assert.EndsWith("screen1", line));
    }
}
