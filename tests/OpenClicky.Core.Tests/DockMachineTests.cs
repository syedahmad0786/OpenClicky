using OpenClicky.Core.CompanionCursor;
using OpenClicky.Core.Drawing;

namespace OpenClicky.Core.Tests;

public class DockMachineTests
{
    static readonly Pt Notch = new(960, 4);

    static DockInput Docked(bool wantsOut = false, bool shown = true, bool reduced = false) =>
        new(CursorPresence.Docked, wantsOut, shown, reduced, new Pt(400, 300), Notch);

    static DockInput Following(bool shown = true) => new(CursorPresence.Following, false, shown, false, new Pt(400, 300), Notch);

    /// <summary>A visible, following companion that has just been told to dock, stepped into Bubble at t=0.</summary>
    static DockMachine Announcing()
    {
        var m = new DockMachine();
        m.PresenceChanged(CursorPresence.Following, CursorPresence.Docked, cursorShown: true);
        Assert.Equal(DockEffect.BubbleStarted, m.Step(0, Docked()));
        Assert.Equal(DockPhase.Bubble, m.Phase);
        return m;
    }

    [Fact]
    public void NeverShownDockedStaysAway()
    {
        var m = new DockMachine();
        m.Step(0, Docked(shown: false));
        Assert.Equal(DockPhase.Gone, m.Phase);
        Assert.False(m.Visible);
    }

    [Fact]
    public void BubbleHoldsThenFliesShrinksAndHides()
    {
        var m = Announcing();
        Assert.Equal(DockEffect.None, m.Step(1.09, Docked()));
        Assert.Equal(DockPhase.Bubble, m.Phase);
        Assert.Equal(DockEffect.BubbleEnded, m.Step(1.1, Docked()));
        Assert.Equal(DockPhase.FlyIn, m.Phase);
        Assert.False(m.Chrome);
        var landed = m.FlightAt(1.1 + 5)!.Value;
        Assert.Equal(Notch, landed);
        m.Step(1.1 + 1.0, Docked()); // past the longest flight
        Assert.Equal(DockPhase.Shrink, m.Phase);
        var (scale, alpha) = m.Look(2.1 + 0.3, false);
        Assert.InRange(scale, 0.02f, 0.99f);
        Assert.InRange(alpha, 0.01f, 0.99f);
        m.Step(2.1 + 0.6, Docked());
        Assert.Equal(DockPhase.Gone, m.Phase);
    }

    [Fact]
    public void ReturningFromAPointDocksWithoutTheBubble()
    {
        var m = new DockMachine();
        Assert.Equal(DockEffect.None, m.Step(0, Docked(wantsOut: true)));
        Assert.Equal(DockPhase.None, m.Phase);
        m.Step(1, Docked());
        Assert.Equal(DockPhase.FlyIn, m.Phase);
    }

    [Fact]
    public void WantingOutDuringTheBubbleCancelsIt()
    {
        var m = Announcing();
        Assert.Equal(DockEffect.BubbleEnded, m.Step(0.5, Docked(wantsOut: true)));
        Assert.Equal(DockPhase.None, m.Phase);
    }

    [Fact]
    public void APointDuringShrinkGrowsBackOutOfTheNotch()
    {
        var m = new DockMachine();
        m.Step(0, Docked());
        m.Step(0.01, Docked());
        m.Step(2, Docked()); // flight over, into Shrink
        Assert.Equal(DockPhase.Shrink, m.Phase);
        Assert.Equal(DockEffect.GrowStarted, m.Step(2.2, Docked(wantsOut: true)));
        Assert.Equal(DockPhase.Grow, m.Phase);
        Assert.True(m.Look(2.2, false).Scale <= 0.02f);
        m.Step(2.2 + 0.31, Docked(wantsOut: true));
        Assert.Equal(DockPhase.None, m.Phase);
        Assert.Equal((1f, 1f), m.Look(3, false));
    }

    [Fact]
    public void GoneGrowsForAPointOrWhenLeavingDocked()
    {
        var m = new DockMachine();
        m.Step(0, Docked(shown: false));
        Assert.Equal(DockEffect.GrowStarted, m.Step(1, Docked(wantsOut: true, shown: false)));
        var leaving = new DockMachine();
        leaving.Step(0, Docked(shown: false));
        Assert.Equal(DockEffect.GrowStarted, leaving.Step(1, Following(shown: false)));
    }

    [Fact]
    public void LeavingDockedMidFlightJustStaysOut()
    {
        var m = new DockMachine();
        m.Step(0, Docked());
        Assert.Equal(DockPhase.FlyIn, m.Phase);
        m.Step(0.1, Following());
        Assert.Equal(DockPhase.None, m.Phase);
    }

    [Fact]
    public void HideMidDockAbandonsItAndTheBubbleEnds()
    {
        var bubble = Announcing();
        Assert.Equal(DockEffect.BubbleEnded, bubble.Interrupt());
        Assert.Equal(DockPhase.Gone, bubble.Phase);

        var flying = new DockMachine();
        flying.Step(0, Docked());
        Assert.Equal(DockEffect.None, flying.Interrupt());
        Assert.Equal(DockPhase.Gone, flying.Phase);
        Assert.Equal(DockEffect.GrowStarted, flying.Step(1, Docked(wantsOut: true, shown: false)));

        var following = new DockMachine();
        Assert.Equal(DockEffect.None, following.Interrupt());
        Assert.Equal(DockPhase.None, following.Phase);
    }

    [Fact]
    public void StaleBubbleRequestDoesNotReplay()
    {
        var m = new DockMachine();
        m.PresenceChanged(CursorPresence.Following, CursorPresence.Docked, cursorShown: true);
        m.PresenceChanged(CursorPresence.Docked, CursorPresence.Following, cursorShown: true); // changed back before a frame
        m.PresenceChanged(CursorPresence.Following, CursorPresence.Docked, cursorShown: false); // and later docked with nothing showing
        Assert.Equal(DockEffect.None, m.Step(0, Docked(shown: true)));
        Assert.Equal(DockPhase.FlyIn, m.Phase); // no bubble
        var off = new DockMachine();
        off.PresenceChanged(CursorPresence.Following, CursorPresence.Docked, cursorShown: true);
        off.Step(0, new DockInput(CursorPresence.Off, false, true, false, default, Notch));
        Assert.Equal(DockEffect.None, off.Step(1, Docked()));
    }

    [Fact]
    public void ReducedMotionFadesInPlaceWithoutAFlight()
    {
        var m = new DockMachine();
        m.Step(0, Docked(reduced: true));
        Assert.Equal(DockPhase.Shrink, m.Phase);
        Assert.Null(m.FlightAt(0.05));
        var (scale, alpha) = m.Look(0.06, true);
        Assert.Equal(1f, scale);
        Assert.InRange(alpha, 0.49f, 0.51f);
        m.Step(0.12, Docked(reduced: true));
        Assert.Equal(DockPhase.Gone, m.Phase);
        m.Step(1, Docked(wantsOut: true, shown: false, reduced: true));
        Assert.Equal(DockPhase.Grow, m.Phase);
        Assert.InRange(m.Look(1.06, true).Alpha, 0.49f, 0.51f);
    }

    [Fact]
    public void OffResetsEverything()
    {
        var m = Announcing();
        Assert.Equal(DockEffect.BubbleEnded, m.Step(0.5, new DockInput(CursorPresence.Off, false, true, false, default, Notch)));
        Assert.Equal(DockPhase.None, m.Phase);
    }

    [Fact]
    public void NotchPointStaysWhenALiveMonitorHoldsIt()
    {
        var monitors = new[] { new Box(0, 0, 1920, 1080), new Box(1920, 0, 1920, 1080) };
        Assert.Equal(new Pt(2880, 4), NotchPoint.Resolve(new Pt(2880, 4), monitors));
    }

    [Fact]
    public void NotchPointOfAnUnpluggedMonitorMovesToTheNearestOne()
    {
        var monitors = new[] { new Box(0, 0, 1920, 1080), new Box(-1280, 0, 1280, 1024) };
        Assert.Equal(new Pt(960, 4), NotchPoint.Resolve(new Pt(2880, 4), monitors));
        Assert.Equal(new Pt(-640, 4), NotchPoint.Resolve(new Pt(-2000, 40), monitors));
        Assert.Equal(new Pt(960, 4), NotchPoint.Resolve(null, [new Box(0, 0, 1920, 1080)]));
        Assert.Equal(new Pt(960, 4), NotchPoint.Resolve(new Pt(float.NaN, 4), [new Box(0, 0, 1920, 1080)]));
        Assert.Equal(new Pt(5, 7), NotchPoint.Resolve(new Pt(5, 7), []));
    }

    [Fact]
    public void ShakeResetForgetsTheSamplesSoADragDoesNotFire()
    {
        var d = new ShakeDetector();
        for (var i = 0; i < 7; i++) Assert.False(d.Add(new Pt(i % 2 == 0 ? 40 : -40, 0), i * 40L));
        d.Reset(); // a mouse button went down
        Assert.False(d.Add(new Pt(-40, 0), 7 * 40L));
        Assert.False(d.Add(new Pt(40, 0), 8 * 40L)); // would have been the sixth reversal
    }
}
