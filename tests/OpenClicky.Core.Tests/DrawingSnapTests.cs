using OpenClicky.Core.Drawing;

namespace OpenClicky.Core.Tests;

public class DrawingSnapTests
{
    private static readonly ScreenCaptureResult Shot = new([0xFF, 0xD8], 1280, 720, 0, 0, 2560, 1440, true, "screen 1");
    private static readonly ScreenText[] Notepad =
    [
        new(1, "Car checklist", new(170, 300, 120, 14)),
        new(1, "Tyres", new(170, 330, 50, 14)),
        new(1, "ENGINE OIL", new(170, 346, 96, 14)),
        new(1, "Wipers", new(170, 362, 60, 14)),
    ];

    private static DrawOp Parse(string tag) => DrawCommandParser.Parse(tag).Ops.Single();

    [Fact]
    public void MisplacedCircleIsRefittedRoundTheWordsItNames()
    {
        var op = Parse("[DRAW:circle id=oil x=150 y=360 r=20 label=\"engine oil\"]"); // the model's guess: left of and below the words
        var snapped = DrawingSnap.Apply([op], Notepad, [Shot]).Single();
        var words = Notepad[2].Bounds;
        Assert.True(snapped.Box.X < words.X && snapped.Box.Right > words.Right, $"box {snapped.Box} does not span the words {words}");
        Assert.True(snapped.Box.Y < words.Y && snapped.Box.Bottom > words.Bottom);
        Assert.InRange(snapped.Box.Centre.X, words.Centre.X - 1, words.Centre.X + 1);
        Assert.InRange(snapped.Box.Centre.Y, words.Centre.Y - 1, words.Centre.Y + 1);
        Assert.Equal(snapped.Box.Centre, snapped.Points.Single());
        Assert.Equal("oil", snapped.Id);
    }

    [Fact]
    public void LabelNamingPartOfALineFitsThoseWordsOnly()
    {
        var op = Parse("[DRAW:rect x=160 y=340 w=200 h=30 label=\"OIL\"]");
        var snapped = DrawingSnap.Apply([op], Notepad, [Shot]).Single();
        Assert.True(snapped.Box.X > Notepad[2].Bounds.X + 40, $"box {snapped.Box} should start at OIL, not ENGINE");
    }

    [Fact]
    public void OcrSlipStillMatches()
    {
        var op = Parse("[DRAW:highlight x=10 y=10 w=40 h=10 label=\"ENGINE OIL\"]");
        var snapped = DrawingSnap.Apply([op], [new(1, "ENGlNE 0IL", new(170, 346, 96, 14))], [Shot]).Single();
        Assert.NotEqual(op.Box, snapped.Box);
    }

    [Fact]
    public void TextBeyondReachIsLeftAlone()
    {
        var op = Parse("[DRAW:circle x=1200 y=700 r=10 label=\"engine oil\"]"); // far corner: another "engine oil" may be meant
        Assert.Same(op, DrawingSnap.Apply([op], Notepad, [Shot]).Single());
    }

    [Theory]
    [InlineData("[DRAW:circle x=150 y=360 r=20]")]                       // no label: nothing to match
    [InlineData("[DRAW:arrow pts=10,10,150,350 label=\"engine oil\"]")]   // arrows keep the model's own line
    [InlineData("[DRAW:circle x=150 y=360 r=20 label=\"spark plugs\"]")] // no such text
    public void OtherOpsPassUnchanged(string tag)
    {
        var op = Parse(tag);
        Assert.Same(op, DrawingSnap.Apply([op], Notepad, [Shot]).Single());
    }

    [Fact]
    public void UnderlineSitsUnderTheWords()
    {
        var snapped = DrawingSnap.Apply([Parse("[DRAW:underline x=100 y=380 w=50 label=\"Wipers\"]")], Notepad, [Shot]).Single();
        Assert.Equal([new Pt(170, 378), new Pt(230, 378)], snapped.Points);
        Assert.Equal("170", snapped.Args["x"]);
        Assert.Equal("60", snapped.Args["w"]);
    }

    [Fact]
    public void PointLandsOnTheNamedText()
    {
        Assert.Equal(Notepad[2].Bounds.Centre, DrawingSnap.PointAt("engine oil", 120, 400, 1, Notepad, Shot));
        Assert.Null(DrawingSnap.PointAt("save button", 120, 400, 1, Notepad, Shot));
    }

    [Fact]
    public void SimilarityIsOneForEqualAndLowForUnrelated()
    {
        Assert.Equal(1, DrawingSnap.Similarity("engine oil", "engine oil"));
        Assert.True(DrawingSnap.Similarity("engine oil", "wipers") < 0.3);
    }
}
