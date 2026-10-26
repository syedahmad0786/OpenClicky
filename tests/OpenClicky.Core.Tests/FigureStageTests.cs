using System.Text.Json;
using OpenClicky.Core.Drawing;

namespace OpenClicky.Core.Tests;

public class FigureStageTests
{
    private static readonly ScreenCaptureResult Shot = new([0xFF, 0xD8], 1280, 720, 0, 0, 2560, 1440, true, "screen 1");

    private static DrawOp Figure(Box box) => FigureBlockParser.ToOp(new FigureSpec("cube", box, null, true, "Cube",
        [new FigurePart(FigureKind.Box, System.Numerics.Vector3.One, default, default, "", null, new Dictionary<string, double>(), null)]));

    [Fact]
    public void BoxPartlyOffScreenIsPulledOnAndKeptReadable()
    {
        var fitted = FigureStage.Fit(Figure(new Box(1200, 680, 60, 60)), [Shot]); // off the bottom right, and tiny
        Assert.True(fitted.Box.W >= 216 && fitted.Box.H >= 216, $"{fitted.Box} is smaller than 30% of 720");
        Assert.True(fitted.Box.Right < 1280 && fitted.Box.Bottom < 720 && fitted.Box.X >= 0 && fitted.Box.Y >= 0, $"{fitted.Box} is not on the screenshot");
        Assert.Equal(fitted.Box, fitted.Figure!.Box); // the overlay reads the spec's box too
    }

    [Fact]
    public void BoxAlreadyOnScreenAndLargeEnoughIsKept()
    {
        var op = Figure(new Box(700, 200, 400, 400));
        Assert.Same(op, FigureStage.Fit(op, [Shot]));
    }

    [Theory]
    [InlineData(560, 280, "in the centre")]
    [InlineData(1000, 280, "on the right side")]
    [InlineData(40, 20, "in the top left")]
    [InlineData(1000, 600, "in the bottom right")]
    public void PlaceIsNamedInWords(float x, float y, string expected) =>
        Assert.Equal(expected, FigureStage.Where(new Box(x, y, 160, 160), Shot));

    [Fact]
    public void Scene3dCubeBecomesASpinningOnScreenFigure()
    {
        using var doc = JsonDocument.Parse("""{"kind":"scene3d","id":"c","title":"Demo cube","shape":"cube","color":"#25664c","rotation":24}""");
        var figure = FigureStage.FromScene3d(doc.RootElement, 1, Shot)!;
        Assert.Equal(FigureKind.Box, Assert.Single(figure.Parts).Kind);
        Assert.True(figure.Spin);
        Assert.Equal("Demo cube", figure.Title);
        Assert.Equal("on the right side", FigureStage.Where(figure.Box, Shot));
        using var chart = JsonDocument.Parse("""{"kind":"chart","id":"c","title":"T","series":[]}""");
        Assert.Null(FigureStage.FromScene3d(chart.RootElement, 1, Shot));
    }

    [Fact]
    public void SentenceNamesTheScreenOnlyWhenThereAreSeveral()
    {
        Assert.Equal("Cube is on the right side of your screen.", new FigurePlacement("Cube", 1, 1, "on the right side").Sentence);
        Assert.Equal("Cube is in the centre of screen 2.", new FigurePlacement("Cube", 2, 2, "in the centre").Sentence);
    }
}
