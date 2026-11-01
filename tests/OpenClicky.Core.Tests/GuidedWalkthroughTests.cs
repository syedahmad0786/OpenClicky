using OpenClicky.Core;

namespace OpenClicky.Core.Tests;

public class GuidedWalkthroughTests
{
    [Fact]
    public void StepWaitAndNext_AdvanceOnlyAfterAVisibleStep()
    {
        var guide = new GuidedWalkthrough();
        var start = guide.Begin("Save the document");
        Assert.Contains("Goal: Save the document", start.Prompt);
        Assert.Equal("Click Save [MARK:circle:10,20,8,8:Save]", guide.Accept(start, "[GUIDE:step] Click Save [MARK:circle:10,20,8,8:Save]"));
        Assert.Equal(1, guide.View.Step);
        Assert.Equal("Click Save", guide.View.Instruction);

        var next = guide.Prepare(WalkthroughMove.Next)!;
        Assert.Contains("Last action: Click Save", next.Prompt);
        Assert.Equal("I cannot see a saved indicator yet.", guide.Accept(next, "[GUIDE:wait] I cannot see a saved indicator yet."));
        Assert.Equal(1, guide.View.Step);
        Assert.False(guide.View.Busy);
        Assert.Equal("I cannot see a saved indicator yet.", guide.View.Instruction);

        var retry = guide.Prepare(WalkthroughMove.Next)!;
        Assert.Contains("Last action: Click Save", retry.Prompt);
        guide.Accept(retry, "[GUIDE:step] Open the File menu.");
        Assert.Equal(2, guide.View.Step);
        Assert.True(guide.View.CanBack);
    }

    [Fact]
    public void StartWaitThenRefresh_FirstSuccessfulInstructionIsStepOne()
    {
        var guide = new GuidedWalkthrough();
        guide.Accept(guide.Begin("Open settings"), "[GUIDE:wait] The target is not visible yet.");

        var refresh = guide.Prepare(WalkthroughMove.Refresh)!;
        guide.Accept(refresh, "[GUIDE:step] Click Settings.");

        Assert.Equal(1, guide.View.Step);
        Assert.Equal("Click Settings.", guide.View.Instruction);
    }

    [Fact]
    public void Goal_IsBoundedWithoutSilentTruncation()
    {
        var guide = new GuidedWalkthrough();
        Assert.Equal(2000, guide.Begin(new string('x', 2000)).Prompt.Split('\n')[0][6..].Length);
        Assert.Throws<ArgumentException>(() => guide.Begin(new string('x', 2001)));
    }

    [Fact]
    public void Back_RegroundsEarlierStepWithoutCachedVisualTags()
    {
        var guide = new GuidedWalkthrough();
        guide.Accept(guide.Begin("Export"), "[GUIDE:step] Click File [POINT:10,20:File]");
        guide.Accept(guide.Prepare(WalkthroughMove.Next)!, "[GUIDE:step] Click Export [MARK:circle:2,3,4,5:Export]");

        var back = guide.Prepare(WalkthroughMove.Back)!;

        Assert.Contains("Freshly ground step 1", back.Prompt);
        Assert.DoesNotContain("[POINT:", back.Prompt);
        Assert.DoesNotContain("[MARK:", back.Prompt);
        guide.Accept(back, "[GUIDE:step] Select File in its current location.");
        Assert.Equal(1, guide.View.Step);
    }

    [Fact]
    public void BackWait_CommitsEarlierStepAndKeepsItsOriginalActionForNext()
    {
        var guide = new GuidedWalkthrough();
        guide.Accept(guide.Begin("Use green"), "[GUIDE:step] Click green.");
        guide.Accept(guide.Prepare(WalkthroughMove.Next)!, "[GUIDE:step] Type the code.");

        var back = guide.Prepare(WalkthroughMove.Back)!;
        guide.Accept(back, "[GUIDE:wait] Green was already clicked; do not click it again.");

        Assert.Equal(1, guide.View.Step);
        Assert.False(guide.View.CanBack);
        Assert.Equal("Earlier step reviewed · Next checks the current result.", guide.View.Status);
        var next = guide.Prepare(WalkthroughMove.Next)!;
        Assert.Contains("Last action: Click green.", next.Prompt);
        Assert.DoesNotContain("Last action: Green was already clicked", next.Prompt);
    }

    [Fact]
    public void RepeatedRefresh_DoesNotEvictStepActions()
    {
        var guide = new GuidedWalkthrough();
        guide.Accept(guide.Begin("Finish"), "[GUIDE:step] First action.");
        guide.Accept(guide.Prepare(WalkthroughMove.Next)!, "[GUIDE:step] Second action.");
        for (var i = 0; i < 12; i++)
            guide.Accept(guide.Prepare(WalkthroughMove.Refresh)!, $"[GUIDE:step] Refreshed view {i}.");

        Assert.True(guide.View.CanBack);
        guide.Accept(guide.Prepare(WalkthroughMove.Back)!, "[GUIDE:wait] Earlier action is complete.");
        Assert.Contains("Last action: First action.", guide.Prepare(WalkthroughMove.Next)!.Prompt);
    }

    [Fact]
    public void HistoryCap_CanBackOnlyWhenPrecedingStepIsRetained()
    {
        var guide = new GuidedWalkthrough();
        guide.Accept(guide.Begin("Many steps"), "[GUIDE:step] Action 1.");
        for (var step = 2; step <= 10; step++)
            guide.Accept(guide.Prepare(WalkthroughMove.Next)!, $"[GUIDE:step] Action {step}.");

        for (var step = 9; step >= 3; step--)
        {
            Assert.True(guide.View.CanBack);
            guide.Accept(guide.Prepare(WalkthroughMove.Back)!, $"[GUIDE:wait] Reviewed {step}.");
            Assert.Equal(step, guide.View.Step);
        }
        Assert.False(guide.View.CanBack);
        Assert.Null(guide.Prepare(WalkthroughMove.Back));
    }

    [Fact]
    public void Complete_StopsButKeepsSummaryVisible()
    {
        var guide = new GuidedWalkthrough();
        var request = guide.Begin("Publish");

        Assert.Equal("Published is visible.", guide.Accept(request, "[GUIDE:complete] Published is visible."));
        Assert.False(guide.View.Active);
        Assert.False(guide.View.Busy);
        Assert.Equal("Published is visible.", guide.View.Instruction);
        Assert.Null(guide.Prepare(WalkthroughMove.Refresh));
    }

    [Theory]
    // MARK/POINT close at their first ']' even after a backslash; DRAW closes outside quotes only.
    [InlineData("[GUIDE:step] Click [MARK:circle:1,2,3,4:C\\] then Save", "Click  then Save")]
    [InlineData("[GUIDE:step] Click [POINT:1,2:dir\\] then Save", "Click  then Save")]
    [InlineData("[GUIDE:step] Press [DRAW:text x=1 y=1 text=\"[OK] \\\" \\]\"] now", "Press now")]
    public void VisualTagsAreStrippedFromInstruction(string raw, string instruction)
    {
        var guide = new GuidedWalkthrough();
        Assert.NotNull(guide.Accept(guide.Begin("Save"), raw));
        Assert.Equal(instruction, guide.View.Instruction);
    }

    [Theory]
    [InlineData("No marker")]
    [InlineData("[GUIDE:maybe] Guess")]
    [InlineData("[GUIDE:step] One [GUIDE:wait] two")]
    [InlineData("[GUIDE:step]")]
    [InlineData("[GUIDE:step] [POINT:1,2:only markup]")]
    [InlineData("[GUIDE:step] [DRAW:text x=1 y=2 text=\"a\\]b\"]")]
    public void InvalidResponse_ExitsBusyAndCanRetry(string raw)
    {
        var guide = new GuidedWalkthrough();
        var request = guide.Begin("Open settings");

        Assert.Null(guide.Accept(request, raw));
        Assert.True(guide.View.Active);
        Assert.False(guide.View.Busy);
        Assert.Equal(0, guide.View.Step);
        Assert.Contains("invalid", guide.View.Status, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(guide.Prepare(WalkthroughMove.Refresh));
    }

    [Fact]
    public void Fail_RestoresNavigationWithoutAdvancing()
    {
        var guide = new GuidedWalkthrough();
        guide.Accept(guide.Begin("Open settings"), "[GUIDE:step] Click Settings.");
        var request = guide.Prepare(WalkthroughMove.Next)!;

        guide.Fail(request, "Network unavailable");

        Assert.Equal(1, guide.View.Step);
        Assert.False(guide.View.Busy);
        Assert.Equal("Network unavailable", guide.View.Status);
        Assert.NotNull(guide.Prepare(WalkthroughMove.Refresh));
    }

    [Fact]
    public void StopAndNewBegin_RejectLateResults()
    {
        var guide = new GuidedWalkthrough();
        var stopped = guide.Begin("First goal");
        guide.Stop();
        Assert.Null(guide.Accept(stopped, "[GUIDE:step] stale"));

        var old = guide.Begin("Second goal");
        var current = guide.Begin("Third goal");
        Assert.Null(guide.Accept(old, "[GUIDE:step] stale"));
        Assert.Equal("current", guide.Accept(current, "[GUIDE:step] current"));
        Assert.Equal("Third goal", guide.View.Goal);
    }

    [Fact]
    public void StopsAtFifteenSteps()
    {
        var guide = new GuidedWalkthrough();
        guide.Accept(guide.Begin("Long task"), "[GUIDE:step] Action 1.");
        for (var step = 2; step <= GuidedWalkthrough.MaximumSteps; step++)
            guide.Accept(guide.Prepare(WalkthroughMove.Next)!, $"[GUIDE:step] Action {step}.");
        Assert.Equal(15, guide.View.Step);
        Assert.True(guide.View.Active);

        Assert.Equal(GuidedWalkthrough.StepLimitMessage, guide.Accept(guide.Prepare(WalkthroughMove.Next)!, "[GUIDE:step] Action 16. [POINT:5,5:x]"));
        Assert.False(guide.View.Active);
        Assert.Equal(15, guide.View.Step);
        Assert.Equal("That's fifteen steps, so I'll stop here. Ask me again to keep going.", guide.View.Instruction);
        Assert.Null(guide.Prepare(WalkthroughMove.Next));
    }

    [Fact]
    public void HoverMarkerSetsHover()
    {
        var guide = new GuidedWalkthrough();
        var start = guide.Begin("Find the tooltip");
        Assert.True(GuidedWalkthrough.AsksForAction("[GUIDE:hover] Rest on the icon."));
        Assert.Equal("Rest on the icon.", guide.Accept(start, "[GUIDE:hover] Rest on the icon."));
        Assert.True(guide.View.StepIsHover);
        Assert.Equal(1, guide.View.Step);

        guide.Accept(guide.Prepare(WalkthroughMove.Next)!, "[GUIDE:step] Now click Details.");
        Assert.False(guide.View.StepIsHover);
        Assert.Equal(2, guide.View.Step);
        Assert.True(GuidedWalkthrough.AsksForAction("[GUIDE:step] x"));
        Assert.False(GuidedWalkthrough.AsksForAction("[GUIDE:wait] x"));
        Assert.False(GuidedWalkthrough.AsksForAction("[GUIDE:complete] x"));
        Assert.False(GuidedWalkthrough.AsksForAction(null));
    }

    [Fact]
    public void EndReasonConsumedOnce()
    {
        var guide = new GuidedWalkthrough();
        guide.Accept(guide.Begin("Export"), "[GUIDE:step] Click File.");
        guide.Stop("the user clicked outside the guided target");
        Assert.Equal("the user clicked outside the guided target", guide.TakeEndReason());
        Assert.Null(guide.TakeEndReason());

        guide.Stop("the user switched apps"); // already stopped: nothing new ended
        Assert.Null(guide.TakeEndReason());
        guide.Accept(guide.Begin("Again"), "[GUIDE:step] Click Edit.");
        guide.Stop(); // the user's own stop has no reason
        Assert.Null(guide.TakeEndReason());
    }

    [Fact]
    public void DrawTagsStrippedFromInstruction()
    {
        var guide = new GuidedWalkthrough();
        Assert.NotNull(guide.Accept(guide.Begin("Save"),
            "[GUIDE:step] Click Save [DRAW:circle x=10 y=20 r=8 label=\"Save\"] at the top [DRAW:clear]"));
        Assert.Equal("Click Save at the top", guide.View.Instruction);
    }

    [Fact]
    public void SetStatusOnlyOnAWaitingStep()
    {
        var guide = new GuidedWalkthrough();
        var start = guide.Begin("Save");
        Assert.False(guide.SetStatus("Still there?")); // busy
        guide.Accept(start, "[GUIDE:step] Click Save.");
        Assert.True(guide.SetStatus("Still there?"));
        Assert.Equal("Still there?", guide.View.Status);
        guide.Stop();
        Assert.False(guide.SetStatus("Still there?"));
    }

    [Fact]
    public async Task RacingAccepts_OnlyOneResultWins()
    {
        var guide = new GuidedWalkthrough();
        var request = guide.Begin("Race");
        var results = await Task.WhenAll(
            Task.Run(() => guide.Accept(request, "[GUIDE:step] first")),
            Task.Run(() => guide.Accept(request, "[GUIDE:step] second")));

        Assert.Single(results, result => result is not null);
        Assert.False(guide.View.Busy);
        Assert.Equal(1, guide.View.Step);
    }
}
