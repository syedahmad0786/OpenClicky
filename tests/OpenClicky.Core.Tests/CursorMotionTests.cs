using OpenClicky.Core.CompanionCursor;
using OpenClicky.Core.Drawing;

namespace OpenClicky.Core.Tests;

public class CursorMotionTests
{
    [Fact]
    public void SpringSettlesWithin800ms()
    {
        var spring = new CursorSpring(new Pt(0, 0));
        Pt p = default;
        for (var i = 0; i < 48; i++) p = spring.Step(new Pt(100, 0), 1.0 / 60); // 0.8 s
        Assert.True(Math.Abs(p.X - 100) < 0.5, $"x={p.X}");
        Assert.True(Math.Abs(p.Y) < 0.5);
    }

    [Fact]
    public void SpringOvershootUnder5Percent()
    {
        var spring = new CursorSpring(new Pt(0, 0));
        float max = 0;
        for (var i = 0; i < 120; i++) max = Math.Max(max, spring.Step(new Pt(100, 0), 1.0 / 60).X);
        Assert.True(max < 105, $"max={max}");
    }

    [Fact]
    public void SpringStableWithHugeDt()
    {
        var spring = new CursorSpring(new Pt(0, 0));
        var p = spring.Step(new Pt(100, 0), 5.0);
        Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y));
        Assert.InRange(p.X, 0.001f, 110f);
    }

    [Fact]
    public void SpringSnapStopsMotion()
    {
        var spring = new CursorSpring(new Pt(0, 0));
        spring.Step(new Pt(100, 0), 0.05);
        spring.Snap(new Pt(7, 9));
        Assert.Equal(new Pt(7, 9), spring.Position);
        Assert.Equal(new Pt(0, 0), spring.Velocity);
    }

    [Fact]
    public void FlightDurationClamped()
    {
        Assert.Equal(0.3, new FlightPath(new Pt(0, 0), new Pt(10, 0)).DurationSeconds, 6);
        Assert.Equal(0.9, new FlightPath(new Pt(0, 0), new Pt(5000, 0)).DurationSeconds, 6);
        Assert.Equal(0.3, new FlightPath(new Pt(3, 3), new Pt(3, 3)).DurationSeconds, 6);
    }

    [Fact]
    public void FlightStartsAndEndsOnEndpoints()
    {
        var f = new FlightPath(new Pt(10, 20), new Pt(410, 320));
        Assert.Equal(new Pt(10, 20), f.At(0));
        Assert.Equal(new Pt(410, 320), f.At(f.DurationSeconds));
        Assert.Equal(new Pt(10, 20), f.At(-1));
        Assert.Equal(new Pt(410, 320), f.At(99));
    }

    [Theory]
    [InlineData(0, 500, 800, 500)]   // left to right
    [InlineData(800, 500, 0, 500)]   // right to left
    [InlineData(400, 100, 400, 700)] // vertical
    public void FlightArcsTowardScreenUp(float x0, float y0, float x1, float y1)
    {
        var f = new FlightPath(new Pt(x0, y0), new Pt(x1, y1));
        var mid = f.At(f.DurationSeconds / 2);
        // Midpoint of the chord is (x0+x1)/2,(y0+y1)/2; the arc bows upward (smaller Y) or, for a vertical flight, sideways.
        if (x0 == x1) Assert.NotEqual((x0 + x1) / 2, mid.X);
        else Assert.True(mid.Y < (y0 + y1) / 2, $"mid.Y={mid.Y}");
        // Control offset is 18 %, a Bezier midpoint is half of that: 9 % of the distance.
        var dist = MathF.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
        var chordMid = new Pt((x0 + x1) / 2, (y0 + y1) / 2);
        var off = MathF.Sqrt((mid.X - chordMid.X) * (mid.X - chordMid.X) + (mid.Y - chordMid.Y) * (mid.Y - chordMid.Y));
        Assert.Equal(0.09f * dist, off, 0.5f);
    }

    [Fact]
    public void FlightIsMonotonicAlongTheChord()
    {
        var f = new FlightPath(new Pt(0, 0), new Pt(1000, 0));
        var last = -1f;
        for (var i = 0; i <= 20; i++)
        {
            var x = f.At(f.DurationSeconds * i / 20).X;
            Assert.True(x >= last);
            last = x;
        }
    }

    static IEnumerable<(Pt, long)> ZigZag(int count, long stepMs, long startMs = 0)
    {
        for (var i = 0; i < count; i++) yield return (new Pt(i % 2 == 0 ? 40 : -40, 0), startMs + i * stepMs);
    }

    [Fact]
    public void ShakeDetectedAfterSixReversals()
    {
        var d = new ShakeDetector();
        var fired = -1;
        var n = 0;
        foreach (var (p, ms) in ZigZag(14, 40))
        {
            if (d.Add(p, ms) && fired < 0) fired = n;
            n++;
        }
        // The first segment sets the direction; the sixth flip lands on the 8th sample (index 7).
        Assert.Equal(7, fired);
    }

    [Fact]
    public void SlowWaveIsNotShake()
    {
        var d = new ShakeDetector();
        foreach (var (p, ms) in ZigZag(14, 150)) Assert.False(d.Add(p, ms)); // the same path over about 2 s
    }

    [Fact]
    public void TinyJitterIsNotShake()
    {
        var d = new ShakeDetector();
        for (var i = 0; i < 40; i++) Assert.False(d.Add(new Pt(i % 2 == 0 ? 3 : -3, 0), i * 10L));
    }

    [Fact]
    public void ShakeCooldownOneSecond()
    {
        var d = new ShakeDetector();
        var fires = new List<long>();
        foreach (var (p, ms) in ZigZag(80, 40)) // 3.2 s of continuous shaking
            if (d.Add(p, ms)) fires.Add(ms);
        Assert.True(fires.Count >= 2, $"fires={fires.Count}");
        for (var i = 1; i < fires.Count; i++) Assert.True(fires[i] - fires[i - 1] >= 1000);
    }

    [Fact]
    public void TriangleTipAtAnchor()
    {
        var tip = new Pt(100, 200);
        var tri = CursorShape.Triangle(tip, 2f, CursorConstants.RestRotationDegrees);
        Assert.Equal(tip, tri[0]);
        float Len(Pt a, Pt b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        var side = CursorConstants.TriangleSide * 2f;
        Assert.Equal(side, Len(tri[0], tri[1]), 0.001f);
        Assert.Equal(side, Len(tri[1], tri[2]), 0.001f);
        Assert.Equal(side, Len(tri[0], tri[2]), 0.001f);
        // The body trails down-right of the tip, so the tip points up-left.
        var cx = (tri[0].X + tri[1].X + tri[2].X) / 3;
        var cy = (tri[0].Y + tri[1].Y + tri[2].Y) / 3;
        Assert.True(cx > tip.X && cy > tip.Y);
    }

    static readonly Box Work = new(0, 0, 1000, 600);

    [Fact]
    public void MessageBubbleFlipsLeftAtRightEdge()
    {
        var normal = BubbleLayout.Message(new Pt(100, 100), 200, 60, Work);
        Assert.Equal(new Box(122, 94, 200, 60), normal);

        var flipped = BubbleLayout.Message(new Pt(900, 100), 200, 60, Work);
        Assert.Equal(900 - 22 - 200, flipped.X);
        Assert.True(flipped.Right <= Work.Right);
        Assert.Equal(94, flipped.Y);
    }

    [Fact]
    public void BubblesClampIntoWorkArea()
    {
        var m = BubbleLayout.Message(new Pt(5, 2), 200, 60, Work);
        Assert.True(m.X >= 0 && m.Y >= 0);
        var l = BubbleLayout.Label(new Pt(990, 595), 120, 30, Work);
        Assert.True(l.Right <= Work.Right && l.Bottom <= Work.Bottom);
    }

    [Fact]
    public void LabelBubbleOffset10Right18Below()
    {
        Assert.Equal(new Box(110, 118, 120, 30), BubbleLayout.Label(new Pt(100, 100), 120, 30, Work));
    }

    [Fact]
    public void BoxHelpers()
    {
        var b = new Box(10, 20, 100, 50);
        Assert.Equal(110, b.Right);
        Assert.Equal(70, b.Bottom);
        Assert.Equal(new Pt(60, 45), b.Centre);
        Assert.True(b.Contains(new Pt(10, 20)));
        Assert.False(b.Contains(new Pt(110, 40)));
        Assert.Equal(new Box(5, 15, 110, 60), b.Inflate(5));
        Assert.Equal(new Box(0, 20, 110, 80), b.Union(new Box(0, 90, 10, 10)));
    }
}
