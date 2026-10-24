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
        if (x0 == x1) Assert.True(mid.X > x0, $"vertical flight should bow right, mid.X={mid.X}");
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

    [Fact]
    public void SpringIgnoresNonFiniteTarget()
    {
        var spring = new CursorSpring(new Pt(10, 20));
        spring.Step(new Pt(100, 0), 0.05);
        var before = spring.Position;
        Assert.Equal(before, spring.Step(new Pt(float.NaN, 5), 0.05));
        Assert.Equal(before, spring.Step(new Pt(5, float.PositiveInfinity), 0.05));
        Assert.True(float.IsFinite(spring.Step(new Pt(100, 0), 0.05).X));
    }

    [Fact]
    public void SpringCtorAndSnapRejectNonFinite()
    {
        var spring = new CursorSpring(new Pt(float.NaN, float.PositiveInfinity));
        Assert.Equal(new Pt(0, 0), spring.Position);
        spring.Snap(new Pt(7, 9));
        spring.Snap(new Pt(float.NaN, 11));
        Assert.Equal(new Pt(7, 11), spring.Position);
        Assert.True(float.IsFinite(spring.Step(new Pt(50, 50), 0.05).X));
    }

    [Fact]
    public void FlightNonFiniteEndpointsAreZeroLength()
    {
        var f = new FlightPath(new Pt(float.NaN, 0), new Pt(40, 50));
        Assert.Equal(0.3, f.DurationSeconds, 6);
        Assert.Equal(new Pt(40, 50), f.At(0.1));
        var g = new FlightPath(new Pt(1, 2), new Pt(float.PositiveInfinity, float.NaN));
        Assert.Equal(new Pt(1, 2), g.At(g.DurationSeconds));
        var h = new FlightPath(new Pt(float.NaN, float.NaN), new Pt(float.NaN, float.NaN));
        Assert.True(double.IsFinite(h.DurationSeconds));
        Assert.Equal(new Pt(0, 0), h.At(0.1));
    }

    [Fact]
    public void FlightAtNonFiniteTimeReturnsAnEndpoint()
    {
        var f = new FlightPath(new Pt(10, 20), new Pt(410, 320));
        Assert.Equal(new Pt(10, 20), f.At(double.NaN));
        Assert.Equal(new Pt(410, 320), f.At(double.PositiveInfinity));
        Assert.Equal(new Pt(10, 20), f.At(double.NegativeInfinity));
    }

    [Fact]
    public void ShakeBlockedAt999msAfterFireAndAllowedAt1000()
    {
        var d = new ShakeDetector();
        Pt Z(int n) => new(n % 2 == 0 ? 40 : -40, 0);
        var fire = -1;
        for (var n = 0; n < 8; n++) if (d.Add(Z(n), n * 40L)) fire = n;
        Assert.Equal(7, fire);
        var t0 = 280L;
        for (var n = 8; n <= 31; n++) Assert.False(d.Add(Z(n), n * 40L)); // up to t0 + 960
        Assert.False(d.Add(Z(32), t0 + 999));
        Assert.True(d.Add(Z(33), t0 + 1000));
    }

    [Fact]
    public void ShakeClockGoingBackwardsResets()
    {
        var d = new ShakeDetector();
        var fires = 0;
        for (var n = 0; n < 8; n++) if (d.Add(new Pt(n % 2 == 0 ? 40 : -40, 0), n * 40L)) fires++;
        Assert.Equal(1, fires);
        // The clock jumps back to 0: the cooldown and old samples are discarded, so the same shake fires again.
        for (var n = 0; n < 8; n++) if (d.Add(new Pt(n % 2 == 0 ? 40 : -40, 0), n * 40L)) fires++;
        Assert.Equal(2, fires);
    }

    [Fact]
    public void BubblesClampOnNegativeOriginWorkAreas()
    {
        var left = new Box(-1920, -200, 1920, 1080); // secondary monitor left of and slightly above the primary
        var label = BubbleLayout.Label(new Pt(-5, 870), 120, 30, left);
        Assert.Equal(new Box(-120, 850, 120, 30), label);
        var msg = BubbleLayout.Message(new Pt(-100, 100), 200, 60, left);
        Assert.Equal(new Box(-322, 94, 200, 60), msg); // flipped left of the tip
        Assert.Equal(new Box(-1920, -200, 200, 60), BubbleLayout.Message(new Pt(-1950, -230), 200, 60, left));

        var above = new Box(0, -1080, 1920, 1080); // monitor above the primary
        Assert.Equal(new Box(32, -1080, 200, 60), BubbleLayout.Message(new Pt(10, -1075), 200, 60, above));
        Assert.Equal(new Box(1800, -30, 120, 30), BubbleLayout.Label(new Pt(1900, -5), 120, 30, above));
    }

    [Fact]
    public void BubblesNeverPropagateNaN()
    {
        var nan = float.NaN;
        foreach (var b in new[]
        {
            BubbleLayout.Label(new Pt(nan, nan), nan, nan, Work),
            BubbleLayout.Message(new Pt(nan, 5), 100, nan, Work),
            BubbleLayout.Label(new Pt(10, 10), 50, 20, new Box(nan, 0, 100, 100)),
            BubbleLayout.Message(new Pt(float.PositiveInfinity, 10), 50, 20, Work),
        })
            Assert.True(float.IsFinite(b.X) && float.IsFinite(b.Y) && float.IsFinite(b.W) && float.IsFinite(b.H), b.ToString());
    }
}
