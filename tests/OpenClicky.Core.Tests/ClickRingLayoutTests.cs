using OpenClicky.Core.CompanionCursor;
using OpenClicky.Core.Drawing;

namespace OpenClicky.Core.Tests;

public class ClickRingLayoutTests
{
    static readonly Box Work = new(0, 0, 1920, 1080);

    [Fact]
    public void NoFrameIsCircle28()
    {
        var (ring, corner, anchor, below) = ClickRingLayout.From(null, new Pt(500, 400), 1.5f, Work);
        Assert.Equal(new Box(500 - 42, 400 - 42, 84, 84), ring); // 28 DIP x 1.5
        Assert.Equal(42, corner);                                // a full circle
        Assert.Equal(new Pt(500, 400 - 42 - 12), anchor);        // 8 DIP x 1.5 above the top centre
        Assert.False(below);
    }

    [Fact]
    public void FrameInflatedBySix()
    {
        var (ring, corner, _, _) = ClickRingLayout.From(new Box(300, 300, 200, 60), new Pt(0, 0), 1, Work);
        Assert.Equal(new Box(294, 294, 212, 72), ring);
        Assert.Equal(10, corner);
    }

    [Fact]
    public void HugeFrameClampedTo180()
    {
        var (ring, _, _, _) = ClickRingLayout.From(new Box(100, 100, 1600, 900), new Pt(0, 0), 1, Work);
        Assert.Equal(360, ring.W);
        Assert.Equal(360, ring.H);
        Assert.Equal(new Pt(900, 550), ring.Centre); // still round the frame's middle
    }

    [Fact]
    public void TinyFrameAtLeast28()
    {
        var (ring, corner, _, _) = ClickRingLayout.From(new Box(400, 400, 4, 4), new Pt(0, 0), 2, Work);
        Assert.Equal(112, ring.W); // 2 x 28 DIP x 2
        Assert.Equal(112, ring.H);
        Assert.Equal(new Pt(402, 402), ring.Centre);
        Assert.Equal(20, corner);  // min(10 DIP x 2, h / 2)
    }

    [Fact]
    public void LabelFlipsBelowAtTopEdge()
    {
        // Ring top at y = 20: no room for an 8 DIP gap plus a label above it.
        var (ring, _, anchor, below) = ClickRingLayout.From(null, new Pt(500, 48), 1, Work);
        Assert.True(below);
        Assert.Equal(new Pt(500, ring.Bottom + 8), anchor);

        // The work area's own top edge counts, not the screen's.
        var (_, _, _, belowInWork) = ClickRingLayout.From(null, new Pt(500, 148), 1, new Box(0, 100, 1920, 980));
        Assert.True(belowInWork);

        var (_, _, _, roomy) = ClickRingLayout.From(null, new Pt(500, 148), 1, Work);
        Assert.False(roomy);
    }

    [Fact]
    public void NonsenseScaleFallsBackToOne()
    {
        Assert.Equal(56, ClickRingLayout.From(null, new Pt(100, 100), float.NaN, Work).Ring.W);
        Assert.Equal(56, ClickRingLayout.From(null, new Pt(100, 100), 0, Work).Ring.W);
    }

    [Fact]
    public void PulseBreathesBetween75And100PercentAndZeroAndThreeDip()
    {
        var faint = ClickRingLayout.Pulse(1.6, reducedMotion: false);   // a whole cycle in: smallest and faintest
        Assert.Equal(0.75f, faint.Alpha, 0.01f);
        Assert.Equal(0, faint.InflateDip, 0.1f);
        var peak = ClickRingLayout.Pulse(0.8, reducedMotion: false);    // half a cycle: brightest and biggest
        Assert.True(peak.Alpha > 0.99f && peak.InflateDip > 2.9f, $"{peak}");
    }

    [Fact]
    public void PulseFadesInOver120MsAndReducedMotionOnlyFades()
    {
        Assert.Equal(0, ClickRingLayout.Pulse(0, false).Alpha);
        Assert.Equal(0.5f, ClickRingLayout.Pulse(0.06, true).Alpha, 0.001f);
        Assert.Equal((1f, 0f), ClickRingLayout.Pulse(5, true));
        Assert.Equal((0f, 0f), ClickRingLayout.Pulse(double.NaN, true));
    }
}
