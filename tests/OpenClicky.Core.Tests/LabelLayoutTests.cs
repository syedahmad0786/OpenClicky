using OpenClicky.Core.Drawing;

namespace OpenClicky.Core.Tests;

public class LabelLayoutTests
{
    private static bool Overlap(Box a, Box b) => a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;

    [Fact]
    public void OverlappingPillsSeparate()
    {
        var work = new Box(0, 0, 1000, 800);
        var pill = new Box(100, 100, 80, 20);
        var resolved = LabelLayout.Resolve([pill, pill, pill with { X = 140 }, new Box(500, 100, 80, 20)], work, dip: 1.5f);
        Assert.Equal(pill, resolved[0]);
        Assert.Equal(pill with { Y = 100 + 20 + 4 * 1.5f }, resolved[1]); // down by its height plus 4 DIP
        Assert.Equal(new Box(500, 100, 80, 20), resolved[3]); // a pill that overlaps nothing stays put
        for (var i = 0; i < resolved.Count; i++)
            for (var j = i + 1; j < resolved.Count; j++)
                Assert.False(Overlap(resolved[i], resolved[j]), $"pills {i} and {j} overlap");
    }

    [Fact]
    public void GivesUpAfterFourTriesAndClampsToTheWorkArea()
    {
        var work = new Box(0, 0, 400, 300);
        var stack = Enumerable.Range(0, 6).Select(i => new Box(0, i * 24, 80, 20)).ToList();
        var resolved = LabelLayout.Resolve([.. stack, new Box(0, 0, 80, 20)], work);
        Assert.Equal(new Box(0, 96, 80, 20), resolved[^1]); // four moves of 24, still overlapping: left there
        Assert.Equal(new Box(320, 280, 80, 20), LabelLayout.Resolve([new Box(390, 299, 80, 20)], work)[0]);
    }
}
