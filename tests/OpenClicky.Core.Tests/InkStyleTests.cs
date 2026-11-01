using OpenClicky.Core.Drawing;

namespace OpenClicky.Core.Tests;

public class InkStyleTests
{
    private static readonly Pt[] Zigzag = [new(10, 10), new(210, 10), new(210, 150), new(40, 300)];

    [Fact]
    public void WobbleIsDeterministic()
    {
        Assert.Equal(InkStyle.Wobble(Zigzag, 7), InkStyle.Wobble(Zigzag, 7));
        Assert.NotEqual(InkStyle.Wobble(Zigzag, 7), InkStyle.Wobble(Zigzag, 8));
        Assert.Equal(InkStyle.Seed("box"), InkStyle.Seed("box")); // stable across runs, unlike string.GetHashCode
        Assert.NotEqual(InkStyle.Seed("box"), InkStyle.Seed("arrow"));
    }

    [Fact]
    public void WobbleStaysWithinAmplitude()
    {
        var wobbled = InkStyle.Wobble(Zigzag, 3, amplitude: 2);
        Assert.True(wobbled.Count >= (int)(InkStyle.Length(Zigzag) / 4), $"resampled every 4 px, got {wobbled.Count} points");
        Assert.All(wobbled, p => Assert.True(DistanceToPolyline(p, Zigzag) <= 2.001f, $"{p} strays from the line"));
        Assert.Contains(wobbled, p => DistanceToPolyline(p, Zigzag) > 0.5f); // it does wobble
        Assert.True(Distance(wobbled[0], Zigzag[0]) <= 2.001f && Distance(wobbled[^1], Zigzag[^1]) <= 2.001f);
    }

    [Fact]
    public void TaperEndsAtFortyPercent()
    {
        Assert.Equal(3.2f * 0.4f, InkStyle.PenWidth(0), 3);
        Assert.Equal(3.2f * 0.4f, InkStyle.PenWidth(1), 3);
        Assert.Equal(3.2f, InkStyle.PenWidth(0.12f), 3);
        Assert.Equal(3.2f, InkStyle.PenWidth(0.5f), 3);
        Assert.Equal(3.2f, InkStyle.PenWidth(0.88f), 3);
        Assert.Equal(10 * 0.7f, InkStyle.PenWidth(0.06f, 10), 3);
        Assert.Equal(14, InkStyle.HighlighterWidth);
        Assert.Equal(0.35f, InkStyle.HighlighterAlpha);
        Assert.Equal(0.18f, InkStyle.WashAlpha);
    }

    [Fact]
    public void RevealCutsAcrossStrokesAndReportsThePenTip()
    {
        IReadOnlyList<IReadOnlyList<Pt>> strokes = [[new(0, 0), new(100, 0)], [new(0, 10), new(100, 10)]];
        var (half, tip) = InkStyle.Reveal(strokes, 0.5f);
        Assert.Equal(new Pt(100, 0), Assert.Single(half)[^1]);
        Assert.Equal(new Pt(100, 0), tip);
        var (most, mostTip) = InkStyle.Reveal(strokes, 0.75f);
        Assert.Equal(2, most.Count);
        Assert.Equal(new Pt(50, 10), mostTip);
        Assert.Empty(InkStyle.Reveal(strokes, 0).Visible);
        Assert.Equal(strokes, InkStyle.Reveal(strokes, 1).Visible);
    }

    private static float Distance(Pt a, Pt b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static float DistanceToPolyline(Pt p, IReadOnlyList<Pt> line)
    {
        var best = float.MaxValue;
        for (var i = 1; i < line.Count; i++)
        {
            Pt a = line[i - 1], b = line[i];
            float dx = b.X - a.X, dy = b.Y - a.Y;
            var t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / (dx * dx + dy * dy), 0, 1);
            best = Math.Min(best, Distance(p, new Pt(a.X + dx * t, a.Y + dy * t)));
        }
        return best;
    }
}
