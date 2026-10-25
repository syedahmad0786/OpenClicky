using OpenClicky.Core;
using OpenClicky.Core.CompanionCursor;
using OpenClicky.Core.Shell;

namespace OpenClicky.Core.Tests;

public class DriveSessionTests
{
    private static DriveSession Session(AgentAccessMode mode, Func<CancellationToken, Task<bool>> ask, TimeSpan? timeout = null) =>
        new(mode, ask, timeout);

    private static async Task<DriveSession> Driving(int x = 100, int y = 100)
    {
        var drive = Session(AgentAccessMode.FullAccess, _ => Task.FromResult(true));
        Assert.True(await drive.EnsureConsentAsync(CancellationToken.None));
        drive.NoteAgentCursor(x, y);
        return drive;
    }

    [Fact]
    public async Task PreviewNeverAsksNorDrives()
    {
        var asked = 0;
        var drive = Session(AgentAccessMode.Preview, _ => { asked++; return Task.FromResult(true); });
        Assert.False(await drive.EnsureConsentAsync(CancellationToken.None));
        Assert.Equal(0, asked);
        Assert.NotEqual(DrivePhase.Driving, drive.Phase);
        Assert.False(drive.OnPhysicalInput(PhysicalKind.Key, 0, 0));
    }

    [Fact]
    public async Task AsksOncePerSession()
    {
        var asked = 0;
        var drive = Session(AgentAccessMode.ConfirmEach, _ => { asked++; return Task.FromResult(true); });
        Assert.True(await drive.EnsureConsentAsync(CancellationToken.None));
        Assert.True(await drive.EnsureConsentAsync(CancellationToken.None));
        Assert.True(await drive.EnsureConsentAsync(CancellationToken.None));
        Assert.Equal(1, asked);
        Assert.Equal(DrivePhase.Driving, drive.Phase);
    }

    [Fact]
    public async Task DeclineEndsDeclined()
    {
        var asked = 0;
        var drive = Session(AgentAccessMode.Auto, _ => { asked++; return Task.FromResult(false); });
        Assert.False(await drive.EnsureConsentAsync(CancellationToken.None));
        Assert.False(await drive.EnsureConsentAsync(CancellationToken.None));
        Assert.Equal(1, asked);
        Assert.Equal(DrivePhase.Ended, drive.Phase);
        Assert.Equal(DriveEnd.Declined, drive.EndedBy);
        drive.End(DriveEnd.Finished); // the first reason sticks
        Assert.Equal(DriveEnd.Declined, drive.EndedBy);
    }

    [Fact]
    public async Task ConsentTimeoutDeclines()
    {
        var never = new TaskCompletionSource<bool>();
        var drive = Session(AgentAccessMode.FullAccess, _ => never.Task, TimeSpan.FromMilliseconds(50));
        Assert.False(await drive.EnsureConsentAsync(CancellationToken.None));
        Assert.Equal(DriveEnd.Declined, drive.EndedBy);
        Assert.Equal(TimeSpan.FromSeconds(60), DriveSession.ConsentTimeout);
    }

    [Fact]
    public async Task AccessSnapshotIsFixed()
    {
        var mode = AgentAccessMode.ConfirmEach;
        var drive = Session(mode, _ => Task.FromResult(true));
        mode = AgentAccessMode.FullAccess; // a later settings change does not reach a running session
        await drive.EnsureConsentAsync(CancellationToken.None);
        drive.End(DriveEnd.Finished);
        Assert.Equal(AgentAccessMode.ConfirmEach, drive.Access);
        Assert.Equal(AgentAccessMode.FullAccess, mode);
    }

    [Fact]
    public async Task ConsentDoesNotChangeAccessMode()
    {
        var drive = Session(AgentAccessMode.Auto, _ => Task.FromResult(true));
        Assert.True(await drive.EnsureConsentAsync(CancellationToken.None));
        Assert.Equal(AgentAccessMode.Auto, drive.Access);
    }

    [Fact]
    public async Task InputWhileAwaitingConsentIsIgnored()
    {
        var answer = new TaskCompletionSource<bool>();
        var drive = Session(AgentAccessMode.ConfirmEach, _ => answer.Task);
        var consent = drive.EnsureConsentAsync(CancellationToken.None);
        Assert.Equal(DrivePhase.AwaitingConsent, drive.Phase);
        // Moving to the card and clicking "Let OpenClicky drive" is physical input: it neither takes back nor ends anything.
        Assert.False(drive.OnPhysicalInput(PhysicalKind.MouseMove, 900, 10));
        Assert.False(drive.OnPhysicalInput(PhysicalKind.MouseButton, 900, 10));
        Assert.False(drive.OnPhysicalInput(PhysicalKind.Key, 900, 10));
        answer.SetResult(true);
        Assert.True(await consent);
        Assert.Equal(DrivePhase.Driving, drive.Phase);
    }

    [Fact]
    public async Task JitterUnder40PxIgnored()
    {
        var drive = await Driving(100, 100);
        Assert.False(drive.OnPhysicalInput(PhysicalKind.MouseMove, 120, 120)); // 28 px
        Assert.False(drive.OnPhysicalInput(PhysicalKind.MouseMove, 139, 100)); // 39 px
        Assert.False(drive.OnPhysicalInput(PhysicalKind.MouseMove, 100, 61));
        Assert.Equal(DrivePhase.Driving, drive.Phase);
    }

    [Fact]
    public async Task FortyPxTakesBack()
    {
        var drive = await Driving(100, 100);
        Assert.True(drive.OnPhysicalInput(PhysicalKind.MouseMove, 124, 132)); // exactly 40 px
        Assert.Equal(DrivePhase.TakenBack, drive.Phase);
        Assert.False(drive.OnPhysicalInput(PhysicalKind.MouseMove, 500, 500)); // already taken back: reported once
    }

    [Fact]
    public async Task ClickOrKeyTakesBack()
    {
        var click = await Driving();
        Assert.True(click.OnPhysicalInput(PhysicalKind.MouseButton, 100, 100));
        Assert.Equal(DrivePhase.TakenBack, click.Phase);
        var key = await Driving();
        Assert.True(key.OnPhysicalInput(PhysicalKind.Key, 100, 100));
        Assert.Equal(DrivePhase.TakenBack, key.Phase);
    }

    [Fact]
    public async Task BaselineResetsAfterAgentMove()
    {
        var drive = await Driving(100, 100);
        drive.BeginAgentAction();
        // The agent's injected move puts the cursor far away: a physical twitch read meanwhile never counts as the user's move.
        Assert.False(drive.OnPhysicalInput(PhysicalKind.MouseMove, 800, 600));
        drive.NoteAgentCursor(800, 600);
        Assert.False(drive.OnPhysicalInput(PhysicalKind.MouseMove, 810, 605));
        Assert.True(drive.OnPhysicalInput(PhysicalKind.MouseMove, 800, 645));
    }

    [Fact]
    public async Task KeyDuringAgentActionStillTakesBack()
    {
        var drive = await Driving();
        drive.BeginAgentAction();
        Assert.True(drive.OnPhysicalInput(PhysicalKind.Key, 100, 100));
    }

    [Fact]
    public async Task RebaseFollowsTheUserWithoutTakingBack()
    {
        var drive = await Driving(100, 100);
        drive.Rebase(900, 20); // the user is on OpenClicky's own card or a confirm dialog
        Assert.False(drive.OnPhysicalInput(PhysicalKind.MouseMove, 910, 25));
        Assert.Equal(DrivePhase.Driving, drive.Phase);
    }

    [Fact]
    public async Task ResumeRestartsWatch()
    {
        var drive = await Driving(100, 100);
        Assert.True(drive.OnPhysicalInput(PhysicalKind.Key, 100, 100));
        drive.Resume(400, 400);
        Assert.Equal(DrivePhase.Driving, drive.Phase);
        Assert.False(drive.OnPhysicalInput(PhysicalKind.MouseMove, 420, 420));
        Assert.True(drive.OnPhysicalInput(PhysicalKind.MouseMove, 400, 440));
    }

    [Fact]
    public async Task ResumeAfterEndDoesNothing()
    {
        var drive = await Driving();
        drive.End(DriveEnd.Stopped);
        drive.Resume(0, 0);
        Assert.Equal(DrivePhase.Ended, drive.Phase);
        Assert.False(drive.OnPhysicalInput(PhysicalKind.Key, 0, 0));
    }

    [Fact]
    public async Task ConsentConfirmDecoratorAsksConsentBeforeInnerAndSkipsInnerOnDecline()
    {
        var order = new List<string>();
        var inner = new FakeConfirm(order);
        var yes = new DriveConsentConfirm(inner, Session(AgentAccessMode.ConfirmEach, _ => { order.Add("consent"); return Task.FromResult(true); }));
        Assert.True(await yes.ConfirmAsync("click: x=1"));
        Assert.True(await yes.ConfirmAsync("type_text: hello"));
        Assert.Equal(["consent", "inner", "inner"], order);

        order.Clear();
        var no = new DriveConsentConfirm(inner, Session(AgentAccessMode.ConfirmEach, _ => { order.Add("consent"); return Task.FromResult(false); }));
        Assert.False(await no.ConfirmAsync("click: x=1"));
        Assert.Equal(["consent"], order);
    }

    [Fact]
    public async Task NonInputConfirmBypassesConsent()
    {
        var order = new List<string>();
        var confirm = new DriveConsentConfirm(new FakeConfirm(order),
            Session(AgentAccessMode.ConfirmEach, _ => { order.Add("consent"); return Task.FromResult(true); }));
        Assert.True(await confirm.ConfirmNonInputAsync("Open Notepad"));
        Assert.Equal(["inner-non-input"], order);
    }

    [Fact]
    public void CardsCarryTheirCopyAndActions()
    {
        var consent = CompanionCards.DriveConsent();
        Assert.Equal((CompanionCards.DriveConsentId, NotchCardKind.NeedsYou), (consent.Id, consent.Kind));
        Assert.Equal("Let OpenClicky use your mouse and keyboard?", consent.Title);
        Assert.Equal("For this task only. Move the mouse or press any key to take over; Esc stops.", consent.Body);
        Assert.Equal([new CardAction(CompanionCards.DriveAction, "Let OpenClicky drive", CardActionStyle.Primary),
            new CardAction(CompanionCards.NotNowAction, "Not now", CardActionStyle.Secondary)], consent.Actions);

        var driving = CompanionCards.Driving();
        Assert.Equal((CompanionCards.DrivingId, NotchCardKind.Activity), (driving.Id, driving.Kind));
        Assert.Equal("OpenClicky is driving", driving.Title);
        Assert.Equal("Esc stops · move the mouse to take over", driving.Body);
        Assert.Equal([new CardAction(CompanionCards.StopAction, "Stop", CardActionStyle.Danger)], driving.Actions);
    }

    private sealed class FakeConfirm(List<string> order) : IAgentConfirm
    {
        public Task<bool> ConfirmAsync(string summary, CancellationToken cancellationToken = default)
        { order.Add("inner"); return Task.FromResult(true); }

        public Task<bool> ConfirmNonInputAsync(string summary, CancellationToken cancellationToken = default)
        { order.Add("inner-non-input"); return Task.FromResult(true); }
    }
}
