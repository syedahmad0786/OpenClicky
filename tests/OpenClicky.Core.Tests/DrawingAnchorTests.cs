using OpenClicky.Core.Drawing;

namespace OpenClicky.Core.Tests;

public class DrawingAnchorTests
{
    private static readonly (int, int, int, int)? Where = (0, 0, 800, 600);
    private const nint Browser = 11, Editor = 22, Shell = 33;

    [Fact]
    public void TurnStartedInOurOwnWindowAnchorsToTheLastForeignWindow()
    {
        var anchor = new DrawingAnchor();
        Assert.False(anchor.IsStale(Browser, Where, ours: false, hasDrawing: false));
        anchor.Capture(Shell, Where, ours: true); // asked from the shell: the browser stays the anchor
        Assert.False(anchor.IsStale(Shell, Where, ours: true, hasDrawing: true)); // our own window never counts
        Assert.False(anchor.IsStale(Browser, Where, ours: false, hasDrawing: true)); // the shell minimised: still lines up
        Assert.True(anchor.IsStale(Editor, Where, ours: false, hasDrawing: true)); // another app came to the front
    }

    [Fact]
    public void AnchorMovingIsStaleAndAClearedDrawingTakesAFreshAnchor()
    {
        var anchor = new DrawingAnchor();
        anchor.Capture(Browser, Where, ours: false);
        Assert.False(anchor.IsStale(Browser, Where, ours: false, hasDrawing: true));
        Assert.True(anchor.IsStale(Browser, (5, 0, 800, 600), ours: false, hasDrawing: true)); // moved
        Assert.False(anchor.IsStale(Editor, Where, ours: false, hasDrawing: false)); // cleared
        Assert.False(anchor.IsStale(Editor, Where, ours: false, hasDrawing: true)); // a drawing with no turn capture anchors here
        Assert.True(anchor.IsStale(Browser, Where, ours: false, hasDrawing: true));
    }

    [Fact]
    public void NoForegroundWindowIsNotASwitch()
    {
        var anchor = new DrawingAnchor();
        anchor.Capture(Browser, Where, ours: false);
        Assert.False(anchor.IsStale(0, null, ours: false, hasDrawing: true));
        Assert.False(anchor.IsStale(Browser, Where, ours: false, hasDrawing: true));
    }
}
