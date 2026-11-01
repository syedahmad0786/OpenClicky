using OpenClicky.Core.Onboarding;
using OpenClicky.Core.Shell;

namespace OpenClicky.Core.Tests;

public class OnboardingFlowTests
{
    private static OnboardingFlow Started()
    {
        var flow = new OnboardingFlow();
        flow.Start();
        return flow;
    }

    [Fact]
    public void StartsInactiveWithNoView()
    {
        var flow = new OnboardingFlow();
        Assert.False(flow.Active);
        Assert.Null(flow.View);
    }

    [Fact]
    public void StartShowsWelcomeOfTwelve()
    {
        var flow = Started();
        Assert.True(flow.Active);
        Assert.Equal(new OnboardingView(OnboardingStep.Welcome, 0, 12, false, false), flow.View);
    }

    [Fact]
    public void NextWalksAllStepsInOrderThenStopsAtFinale()
    {
        var flow = Started();
        Assert.Equal(Enum.GetValues<OnboardingStep>(), OnboardingFlow.Steps);
        for (var i = 1; i < 12; i++)
        {
            Assert.True(flow.Next());
            Assert.Equal(OnboardingFlow.Steps[i], flow.Step);
        }
        Assert.Equal(OnboardingStep.Finale, flow.Step);
        Assert.False(flow.Next());
        Assert.Equal(OnboardingStep.Finale, flow.Step);
    }

    [Fact]
    public void BackStopsAtWelcome()
    {
        var flow = Started();
        Assert.False(flow.Back());
        Assert.True(flow.Next());
        Assert.True(flow.Back());
        Assert.Equal(OnboardingStep.Welcome, flow.Step);
        Assert.False(flow.Back());
    }

    [Fact]
    public void NextAndBackBlockedWhileBusy()
    {
        var flow = Started();
        flow.Next();
        flow.Busy = true;
        Assert.False(flow.Next());
        Assert.False(flow.Back());
        Assert.Equal(OnboardingStep.Colour, flow.Step);
        flow.Busy = false;
        Assert.True(flow.Next());
    }

    [Fact]
    public void MarkDoneOnlyForCurrentStep()
    {
        var flow = Started();
        Assert.False(flow.MarkDone(OnboardingStep.TextMode));
        while (flow.Step != OnboardingStep.TextMode) flow.Next();
        Assert.True(flow.MarkDone(OnboardingStep.TextMode));
        Assert.True(flow.StepDone);
        Assert.False(flow.MarkDone(OnboardingStep.TextMode));
    }

    [Fact]
    public void MovingResetsStepDone()
    {
        var flow = Started();
        Assert.True(flow.MarkDone(OnboardingStep.Welcome));
        Assert.True(flow.Next());
        Assert.False(flow.StepDone);
        Assert.True(flow.MarkDone(OnboardingStep.Colour));
        Assert.True(flow.Back());
        Assert.False(flow.StepDone);
    }

    [Fact]
    public void ActionsIgnoredWhenInactive()
    {
        var flow = new OnboardingFlow();
        Assert.False(flow.Next());
        Assert.False(flow.Back());
        Assert.False(flow.MarkDone(OnboardingStep.Welcome));
        flow.Busy = true;
        Assert.False(flow.Busy);
        Assert.Null(flow.View);
    }

    [Fact]
    public void StartResetsToWelcome()
    {
        var flow = Started();
        flow.Next();
        flow.Next();
        flow.MarkDone(OnboardingStep.Microphone);
        flow.Busy = true;
        flow.Start();
        Assert.Equal(new OnboardingView(OnboardingStep.Welcome, 0, 12, false, false), flow.View);
    }

    [Fact]
    public void EndClearsBusy()
    {
        var flow = Started();
        flow.Busy = true;
        flow.End();
        Assert.False(flow.Active);
        Assert.False(flow.Busy);
        Assert.Null(flow.View);
        flow.Start();
        Assert.False(flow.Busy);
    }
}
