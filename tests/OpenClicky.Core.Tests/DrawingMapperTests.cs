using OpenClicky.Core.Drawing;

namespace OpenClicky.Core.Tests;

public class DrawingMapperTests
{
    // Two 100x50 screenshots of 200x100 monitors side by side; the second one holds the cursor.
    private static readonly ScreenCaptureResult[] Screens =
    [
        new([0xff, 0xd8], 100, 50, 0, 0, 200, 100, false, "screen 1"),
        new([0xff, 0xd8], 100, 50, 200, 0, 200, 100, true, "screen 2"),
    ];

    private static IReadOnlyList<(DrawOp Virtual, DrawOp Source, float Scale)> Map(string reply) =>
        DrawingMapper.ToVirtual(DrawCommandParser.Parse(reply).Ops, Screens);

    [Fact]
    public void MapsScreenTwo()
    {
        var mapped = Map("[DRAW:dot x=10 y=20 screen=1][DRAW:rect x=10 y=20 w=30 h=10]");
        Assert.Equal(2, mapped.Count);
        Assert.Equal([new Pt(20, 40)], mapped[0].Virtual.Points);
        Assert.Equal(1, mapped[0].Source.Screen);
        // No screen key: the cursor monitor (screen 2).
        var (virt, source, scale) = mapped[1];
        Assert.Equal(new Box(220, 40, 60, 20), virt.Box);
        Assert.Equal(new Box(10, 20, 30, 10), source.Box);
        Assert.Equal(2, source.Screen);
        Assert.Equal(2, virt.Screen);
        Assert.Equal(2f, scale);
    }

    [Theory]
    [InlineData("dot x=100 y=1")]
    [InlineData("dot x=1 y=50")]
    [InlineData("rect x=90 y=1 w=20 h=5")]
    [InlineData("ring x=3 y=20 r=5")]
    [InlineData("line pts=1,1,40,60")]
    [InlineData("dot x=1 y=1 screen=3")]
    public void DropsOutOfBounds(string tag)
    {
        var mapped = Map($"[DRAW:{tag}][DRAW:dot x=99 y=49]");
        var kept = Assert.Single(mapped);
        Assert.Equal([new Pt(398, 98)], kept.Virtual.Points);
        Assert.Empty(DrawingMapper.ToVirtual(DrawCommandParser.Parse("[DRAW:dot x=1 y=1]").Ops, []));
    }

    [Fact]
    public void ScalesRadiusByMonitorRatio()
    {
        var (virt, source, scale) = Assert.Single(Map("[DRAW:ring x=20 y=20 r=5]"));
        Assert.Equal(new Box(15, 15, 10, 10), source.Box);
        Assert.Equal(new Box(230, 30, 20, 20), virt.Box);
        Assert.Equal([new Pt(240, 40)], virt.Points);
        Assert.Equal(2f, scale);
    }

    [Fact]
    public void EditsPassThrough()
    {
        var ops = DrawCommandParser.Parse("[DRAW:move id=a dx=5 dy=-3][DRAW:undo][DRAW:clear]").Ops;
        var mapped = DrawingMapper.ToVirtual(ops, []);
        Assert.Equal(3, mapped.Count);
        Assert.All(mapped, m => { Assert.Same(m.Source, m.Virtual); Assert.Equal(1f, m.Scale); });
        Assert.Same(ops[0], mapped[0].Virtual);
    }
}
