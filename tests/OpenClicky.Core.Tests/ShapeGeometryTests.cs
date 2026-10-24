using OpenClicky.Core.Drawing;

namespace OpenClicky.Core.Tests;

public class ShapeGeometryTests
{
    private static DrawOp Op(string tag) => Assert.Single(DrawCommandParser.Parse(tag).Ops);

    [Fact]
    public void CircleHas64Points()
    {
        var ring = Assert.Single(ShapeGeometry.Outline(Op("[DRAW:circle x=100 y=50 r=20]")));
        Assert.Equal(64, ring.Count);
        Assert.Equal(ring[0], ring[^1]); // closed
        Assert.All(ring, p => Assert.Equal(20, MathF.Sqrt((p.X - 100) * (p.X - 100) + (p.Y - 50) * (p.Y - 50)), 2));
        Assert.Equal(64, Assert.Single(ShapeGeometry.Outline(Op("[DRAW:circle x=0 y=0 w=40 h=20]"))).Count);
        Assert.True(ShapeGeometry.IsClosed(Op("[DRAW:circle x=100 y=50 r=20]")));
        Assert.False(ShapeGeometry.IsClosed(Op("[DRAW:line pts=0,0,10,10]")));
    }

    [Fact]
    public void ArrowHeadAngle()
    {
        var outline = ShapeGeometry.Outline(Op("[DRAW:arrow pts=10,100,110,100]"), dip: 1.5f);
        Assert.Equal(2, outline.Count);
        Assert.Equal([new Pt(10, 100), new Pt(110, 100)], outline[0]);
        var head = outline[1];
        Assert.Equal(3, head.Count);
        Assert.Equal(new Pt(110, 100), head[1]); // the head meets at the tip
        foreach (var barb in new[] { head[0], head[2] })
        {
            float dx = barb.X - 110, dy = barb.Y - 100;
            Assert.Equal(12 * 1.5f, MathF.Sqrt(dx * dx + dy * dy), 3); // 12 DIP
            Assert.Equal(28, MathF.Atan2(MathF.Abs(dy), -dx) * 180 / MathF.PI, 2); // 28° off the shaft, pointing back
        }
        Assert.NotEqual(head[0].Y, head[2].Y); // one barb either side
    }

    [Fact]
    public void PathSubsetFlattens()
    {
        var outline = ShapeGeometry.Outline(Op("""[DRAW:path d="M10 10 L50 10 Q90 10 90 50 C90 70 70 90 50 90 Z M200 200 L220 220"]"""));
        Assert.Equal(2, outline.Count);
        var first = outline[0];
        Assert.Equal(new Pt(10, 10), first[0]);
        Assert.Contains(new Pt(50, 10), first);
        Assert.Contains(new Pt(90, 50), first);  // the quadratic ends on its end point
        Assert.Contains(new Pt(50, 90), first);  // and so does the cubic
        Assert.Equal(new Pt(10, 10), first[^1]); // Z closes back to the subpath start
        Assert.True(first.Count >= 3 + 16 + 16);
        Assert.All(first.Skip(2).Take(16), p => Assert.True(p.X >= 50 && p.X <= 90 && p.Y >= 10 && p.Y <= 50)); // the curve stays in its hull
        Assert.Equal([new Pt(200, 200), new Pt(220, 220)], outline[1]);
    }

    [Fact]
    public void TraceIsClosed()
    {
        var trace = Assert.Single(ShapeGeometry.Outline(Op("[DRAW:trace pts=10,10,100,20,90,90,20,80]")));
        Assert.Equal(4 * 16 + 1, trace.Count);
        Assert.Equal(trace[0], trace[^1]);
        Assert.Equal(new Pt(10, 10), trace[0]);   // the spline passes through every point
        Assert.Equal(new Pt(100, 20), trace[16]);
        Assert.True(ShapeGeometry.IsClosed(Op("[DRAW:trace pts=10,10,100,20,90,90]")));
    }

    [Fact]
    public void FrameBracketsCappedAt24()
    {
        var big = ShapeGeometry.Outline(Op("[DRAW:frame x=0 y=0 w=400 h=300]"));
        Assert.Equal(4, big.Count);
        Assert.Equal([new Pt(0, 24), new Pt(0, 0), new Pt(24, 0)], big[0]); // 25 % of 300/400 would be 75/100
        var small = ShapeGeometry.Outline(Op("[DRAW:frame x=0 y=0 w=40 h=60]"));
        Assert.Equal([new Pt(0, 15), new Pt(0, 0), new Pt(10, 0)], small[0]); // 25 % of each side
        var scaled = ShapeGeometry.Outline(Op("[DRAW:frame x=0 y=0 w=400 h=300]"), dip: 2);
        Assert.Equal(new Pt(48, 0), scaled[0][2]);
        Assert.All(big, bracket => Assert.Equal(3, bracket.Count));
    }

    [Fact]
    public void SmallShapesHaveTheirOutlines()
    {
        Assert.Equal(11, Assert.Single(ShapeGeometry.Outline(Op("[DRAW:star x=50 y=50]"))).Count); // 10 points, closed
        Assert.Equal(2, ShapeGeometry.Outline(Op("[DRAW:cross x=50 y=50]")).Count);
        Assert.Equal(3, Assert.Single(ShapeGeometry.Outline(Op("[DRAW:check x=50 y=50]"))).Count);
        Assert.Equal([new Pt(10, 40), new Pt(110, 40)], Assert.Single(ShapeGeometry.Outline(Op("[DRAW:underline x=10 y=40 w=100]"))));
        Assert.Equal([new Pt(10, 40), new Pt(110, 40)], Assert.Single(ShapeGeometry.Outline(Op("[DRAW:strike x=10 y=40 w=100]"))));
        var rect = Assert.Single(ShapeGeometry.Outline(Op("[DRAW:rect x=0 y=0 w=100 h=50]")));
        Assert.Equal(rect[0], rect[^1]);
        Assert.All(rect, p => Assert.True(p.X >= 0 && p.X <= 100 && p.Y >= 0 && p.Y <= 50));
        Assert.Empty(ShapeGeometry.Outline(Op("[DRAW:dot x=5 y=5]")));
        Assert.Empty(ShapeGeometry.Outline(Op("""[DRAW:text x=5 y=5 text="hi"]""")));
    }
}
