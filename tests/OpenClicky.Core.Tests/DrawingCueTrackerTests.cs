using OpenClicky.Core.Drawing;

namespace OpenClicky.Core.Tests;

public class DrawingCueTrackerTests
{
    [Fact]
    public void TagAfterFirstSentenceCuesLineOne()
    {
        var cues = new DrawingCueTracker();
        cues.Update("[DRAW:dot id=a x=1 y=1] First line. [DRAW:dot id=b x=2 y=2] Second line.");
        Assert.Equal(["a"], cues.TakeThrough(0).Select(o => o.Id));
        Assert.Equal(["b"], cues.TakeThrough(1).Select(o => o.Id));
    }

    [Fact]
    public void TagsTakenOnce()
    {
        var cues = new DrawingCueTracker();
        cues.Update("One. [DRAW:dot x=1 y=1]");
        cues.Update("One. [DRAW:dot x=1 y=1] Two.");
        Assert.Single(cues.TakeThrough(1));
        Assert.Empty(cues.TakeThrough(1));
        Assert.Empty(cues.TakeAll());
    }

    [Fact]
    public void PartialTagNeverEmitted()
    {
        var cues = new DrawingCueTracker();
        cues.Update("Look. [DRAW:dot x=1 y=");
        Assert.Empty(cues.TakeThrough(5));
        Assert.Empty(cues.TakeAll());
        cues.Update("Look. [DRAW:dot x=1 y=2]");
        Assert.Equal(2, Assert.Single(cues.TakeAll()).Points[0].Y);
    }

    [Fact]
    public void TakeAllReturnsRemaining()
    {
        var cues = new DrawingCueTracker();
        cues.Update("[DRAW:dot id=a x=1 y=1] A. [DRAW:dot id=b x=1 y=1] B! [DRAW:dot id=c x=1 y=1] C?");
        Assert.Equal(["a"], cues.TakeThrough(0).Select(o => o.Id));
        Assert.Equal(["b", "c"], cues.TakeAll().Select(o => o.Id));
        Assert.Empty(cues.TakeAll());
    }
}
