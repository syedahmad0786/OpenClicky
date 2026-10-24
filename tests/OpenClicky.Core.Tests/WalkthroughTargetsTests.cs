using OpenClicky.Core;
using OpenClicky.Core.CompanionCursor;
using OpenClicky.Core.Drawing;
using OpenClicky.Core.Shell;

namespace OpenClicky.Core.Tests;

public class WalkthroughTargetsTests
{
    private static readonly Box Monitor = new(0, 0, 1920, 1080);
    private static GuidedTarget Button(bool hover = false) => GuidedTargets.From(new Pt(150, 120), new Box(100, 100, 100, 40), "Save", hover);

    [Fact]
    public void ClickInsideInflatedFrameAdvances()
    {
        var watch = new WalkthroughTargetWatch();
        watch.Arm(Button(), 0);
        // 5 px left of the frame: inside the 6 px slop. Advancing uses the target up.
        Assert.Equal(TargetEvent.Advance, watch.OnClick(new Pt(95, 105), onOwnWindow: false, 1_000));
        Assert.Null(watch.Target);
        Assert.Equal(TargetEvent.None, watch.OnClick(new Pt(95, 105), false, 1_100));

        // Without a frame, anything within the 28 px radius of the centre counts.
        watch.Arm(GuidedTargets.From(new Pt(500, 500), null, "", false), 2_000);
        Assert.Equal(TargetEvent.Advance, watch.OnClick(new Pt(520, 518), false, 2_100));
    }

    [Fact]
    public void ClickElsewhereLeavesGuide()
    {
        var watch = new WalkthroughTargetWatch();
        watch.Arm(Button(), 0);
        Assert.Equal(TargetEvent.LeftGuide, watch.OnClick(new Pt(93, 105), false, 500)); // 7 px out: past the slop
        Assert.Null(watch.Target);
    }

    [Fact]
    public void OwnWindowClickIgnored()
    {
        var watch = new WalkthroughTargetWatch();
        watch.Arm(Button(), 0);
        // A click on the step card (Back, Stop) or the shell: neither leaves nor advances, wherever it lands.
        Assert.Equal(TargetEvent.None, watch.OnClick(new Pt(1500, 900), onOwnWindow: true, 100));
        Assert.Equal(TargetEvent.None, watch.OnClick(new Pt(150, 120), onOwnWindow: true, 200));
        Assert.NotNull(watch.Target);
        Assert.Equal(TargetEvent.Advance, watch.OnClick(new Pt(150, 120), false, 300));
    }

    [Fact]
    public void HoverCompletesAfterTwoSeconds()
    {
        var watch = new WalkthroughTargetWatch();
        watch.Arm(Button(hover: true), 0);
        Assert.Equal(TargetEvent.None, watch.OnPointer(new Pt(150, 120), 100));
        Assert.Equal(TargetEvent.None, watch.OnPointer(new Pt(160, 125), 2_099));
        Assert.Equal(TargetEvent.Advance, watch.OnPointer(new Pt(160, 125), 2_100));
        Assert.Null(watch.Target);

        // A click target ignores the pointer resting on it.
        watch.Arm(Button(hover: false), 3_000);
        Assert.Equal(TargetEvent.None, watch.OnPointer(new Pt(150, 120), 3_000));
        Assert.Equal(TargetEvent.None, watch.OnPointer(new Pt(150, 120), 9_000));
    }

    [Fact]
    public void HoverResetsWhenLeaving()
    {
        var watch = new WalkthroughTargetWatch();
        watch.Arm(Button(hover: true), 0);
        watch.OnPointer(new Pt(150, 120), 0);
        Assert.Equal(TargetEvent.None, watch.OnPointer(new Pt(400, 400), 1_500));
        Assert.Equal(TargetEvent.None, watch.OnPointer(new Pt(150, 120), 1_600));
        Assert.Equal(TargetEvent.None, watch.OnPointer(new Pt(150, 120), 3_500));
        Assert.Equal(TargetEvent.Advance, watch.OnPointer(new Pt(150, 120), 3_600));
    }

    [Fact]
    public void ArmedTargetExpiresAfter120s()
    {
        var watch = new WalkthroughTargetWatch();
        watch.Arm(Button(), 1_000);
        Assert.Equal(TargetEvent.None, watch.Tick(120_999));
        Assert.Equal(TargetEvent.Expired, watch.Tick(121_000));
        Assert.Equal(TargetEvent.None, watch.Tick(121_100)); // once per arm
        // The walkthrough stays: the highlighted control still advances it.
        Assert.Equal(TargetEvent.Advance, watch.OnClick(new Pt(150, 120), false, 130_000));
    }

    [Fact]
    public void GuideTimesOutAfter600sIdle()
    {
        var watch = new WalkthroughTargetWatch();
        watch.Arm(Button(), 0);
        Assert.Equal(TargetEvent.Advance, watch.OnClick(new Pt(150, 120), false, 100_000));
        watch.Arm(Button(), 105_000); // re-arming is not progress: the idle clock runs from the advance
        Assert.Equal(TargetEvent.Expired, watch.Tick(225_000));
        Assert.Equal(TargetEvent.None, watch.Tick(699_999));
        Assert.Equal(TargetEvent.GuideTimedOut, watch.Tick(700_000));
        Assert.Null(watch.Target);
        Assert.Equal(TargetEvent.None, watch.Tick(800_000));
    }

    [Fact]
    public void IdleClockStartsWhenTheWalkthroughStartsAndRestartsDuringATask()
    {
        var watch = new WalkthroughTargetWatch();
        watch.Activate(0); // active, nothing pointed yet
        watch.Activate(50_000); // already running: unchanged
        Assert.Equal(TargetEvent.None, watch.Tick(599_999));
        Assert.Equal(TargetEvent.GuideTimedOut, watch.Tick(600_000));

        watch.Activate(1_000_000);
        watch.RestartIdle(1_500_000); // an agent task ran until here
        Assert.Equal(TargetEvent.None, watch.Tick(1_600_000));
        Assert.Equal(TargetEvent.GuideTimedOut, watch.Tick(2_100_000));
    }

    [Fact]
    public void WalkthroughCardAddedOnlyForAStartOrANewStep()
    {
        static WalkthroughView V(int step, bool busy = false, bool active = true) => new(active, busy, step, false, "Goal", "Do it", "Step");
        Assert.True(WalkthroughCardPolicy.AddsCard(-1, V(0, busy: true))); // started
        Assert.False(WalkthroughCardPolicy.AddsCard(0, V(0, busy: true)));
        Assert.True(WalkthroughCardPolicy.AddsCard(0, V(1))); // step 1 arrived
        Assert.False(WalkthroughCardPolicy.AddsCard(1, V(1, busy: true))); // loading the next one: update in place
        Assert.False(WalkthroughCardPolicy.AddsCard(1, V(1))); // a wait or recheck reply, a status: update in place
        Assert.True(WalkthroughCardPolicy.AddsCard(1, V(2)));
        Assert.True(WalkthroughCardPolicy.AddsCard(3, V(2))); // Back
        Assert.False(WalkthroughCardPolicy.AddsCard(-1, V(2, active: false)));
    }

    [Fact]
    public void AppSwitchAllowedOnlyRightAfterAnAdvance()
    {
        var watch = new WalkthroughTargetWatch();
        watch.Arm(Button(), 0);
        Assert.False(watch.InSwitchGrace(10));
        watch.OnClick(new Pt(150, 120), false, 1_000);
        Assert.True(watch.InSwitchGrace(3_999));
        Assert.False(watch.InSwitchGrace(4_000));
        watch.Reset();
        Assert.False(watch.InSwitchGrace(1_500));
    }

    [Fact]
    public void SnapPicksFirstInteractiveAncestor()
    {
        var centre = new Pt(150, 120);
        var chain = new List<SnapCandidate>
        {
            new(new Box(140, 110, 20, 20), false, false), // the button's text
            new(new Box(100, 100, 100, 40), true, false), // the button
            new(new Box(0, 0, 800, 600), true, false), // a list item round it
        };
        Assert.Equal(new Box(100, 100, 100, 40), TargetSnap.Choose(chain, centre, Monitor));
        // An interactive element that does not contain the point is skipped.
        Assert.Equal(new Box(0, 0, 800, 600),
            TargetSnap.Choose([new(new Box(300, 300, 50, 20), true, false), chain[2]], centre, Monitor));
        Assert.Null(TargetSnap.Choose([chain[0]], centre, Monitor));
        Assert.Null(TargetSnap.Choose([], centre, Monitor));
    }

    [Fact]
    public void SnapRejectsHugeOrOwnProcess()
    {
        var centre = new Pt(150, 120);
        Assert.Null(TargetSnap.Choose([new(new Box(100, 100, 100, 40), true, OwnProcess: true)], centre, Monitor));
        Assert.Null(TargetSnap.Choose([new(new Box(0, 0, 1000, 600), true, false)], centre, Monitor)); // 29 % of the monitor
        Assert.Equal(new Box(0, 0, 960, 540), TargetSnap.Choose([new(new Box(0, 0, 960, 540), true, false)], centre, Monitor)); // exactly 25 %
        Assert.Null(TargetSnap.Choose([new(new Box(float.PositiveInfinity, 0, float.NegativeInfinity, 10), true, false)], centre, Monitor));
    }

    [Fact]
    public void RadiusClamped28To180()
    {
        var c = new Pt(10, 10);
        Assert.Equal(28, GuidedTargets.From(c, new Box(0, 0, 10, 10), "", false).Radius);
        Assert.Equal(56, GuidedTargets.From(c, new Box(0, 0, 100, 60), "", false).Radius);
        Assert.Equal(180, GuidedTargets.From(c, new Box(0, 0, 1000, 400), "", false).Radius);
        var bare = GuidedTargets.From(c, null, null, true);
        Assert.Equal(28, bare.Radius);
        Assert.False(bare.HasFrame);
        Assert.True(bare.IsHover);
        Assert.Equal("", bare.Label);
    }

    [Fact]
    public void WalkthroughCardOffersBackOnlyWhenPossible()
    {
        var card = CompanionCards.Walkthrough(new WalkthroughView(true, false, 3, true, "Goal", new string('a', 1_200), "Step 3"));
        Assert.Equal(("walkthrough", NotchCardKind.Activity, "Step 3"), (card.Id, card.Kind, card.Title));
        Assert.Equal(1_000, card.Body!.Length);
        Assert.Equal(["back", "recheck", "stop"], card.Actions.Select(a => a.Id));
        Assert.Equal(CardActionStyle.Danger, card.Actions[^1].Style);

        var first = CompanionCards.Walkthrough(new WalkthroughView(true, true, 0, false, "Goal", "", "Looking at the screen…"));
        Assert.Equal("Guide", first.Title);
        Assert.Equal("Looking at the screen…", first.Body);
        Assert.Equal(["recheck", "stop"], first.Actions.Select(a => a.Id));
    }
}
