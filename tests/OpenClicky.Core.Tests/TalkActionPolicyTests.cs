using OpenClicky.Core;

namespace OpenClicky.Core.Tests;

public class TalkActionPolicyTests
{
    [Theory]
    [InlineData(AgentAccessMode.Preview, TalkActionKind.Model, TalkActionDecision.Execute)]
    [InlineData(AgentAccessMode.ConfirmEach, TalkActionKind.Model, TalkActionDecision.Execute)]
    [InlineData(AgentAccessMode.Preview, TalkActionKind.Open, TalkActionDecision.PreviewOnly)]
    [InlineData(AgentAccessMode.Preview, TalkActionKind.Copy, TalkActionDecision.PreviewOnly)]
    [InlineData(AgentAccessMode.Preview, TalkActionKind.Type, TalkActionDecision.PreviewOnly)]
    [InlineData(AgentAccessMode.ConfirmEach, TalkActionKind.Open, TalkActionDecision.AskFirst)]
    [InlineData(AgentAccessMode.ConfirmEach, TalkActionKind.Copy, TalkActionDecision.Execute)]
    [InlineData(AgentAccessMode.ConfirmEach, TalkActionKind.Type, TalkActionDecision.AskFirst)]
    [InlineData(AgentAccessMode.Auto, TalkActionKind.Open, TalkActionDecision.Execute)]
    [InlineData(AgentAccessMode.Auto, TalkActionKind.Copy, TalkActionDecision.Execute)]
    [InlineData(AgentAccessMode.Auto, TalkActionKind.Type, TalkActionDecision.AskFirst)]
    [InlineData(AgentAccessMode.FullAccess, TalkActionKind.Open, TalkActionDecision.Execute)]
    [InlineData(AgentAccessMode.FullAccess, TalkActionKind.Copy, TalkActionDecision.Execute)]
    [InlineData(AgentAccessMode.FullAccess, TalkActionKind.Type, TalkActionDecision.Execute)]
    public void DecidesPerModeAndKind(AgentAccessMode mode, TalkActionKind kind, TalkActionDecision expected) =>
        Assert.Equal(expected, TalkActionPolicy.Decide(new TalkAction(kind, "x"), mode));

    [Theory]
    [InlineData(AgentAccessMode.ConfirmEach)]
    [InlineData(AgentAccessMode.Auto)]
    [InlineData(AgentAccessMode.FullAccess)]
    public void TypeIsOnlyPreviewedWhileAnAgentTaskRuns(AgentAccessMode mode)
    {
        Assert.Equal(TalkActionDecision.PreviewOnly, TalkActionPolicy.Decide(new TalkAction(TalkActionKind.Type, "x"), mode, agentTaskRunning: true));
        Assert.Equal(TalkActionPolicy.Decide(new TalkAction(TalkActionKind.Open, "x"), mode),
            TalkActionPolicy.Decide(new TalkAction(TalkActionKind.Open, "x"), mode, agentTaskRunning: true));
        Assert.Equal(TalkActionDecision.Execute, TalkActionPolicy.Decide(new TalkAction(TalkActionKind.Copy, "x"), mode, agentTaskRunning: true));
    }

    [Fact]
    public void NoSnapshotMeansPreview() =>
        Assert.Equal(AgentAccessMode.Preview, TalkActionPolicy.EffectiveMode(null, AgentAccessMode.FullAccess));

    [Fact]
    public void ANarrowedModeApplies() =>
        Assert.Equal(AgentAccessMode.ConfirmEach, TalkActionPolicy.EffectiveMode(AgentAccessMode.FullAccess, AgentAccessMode.ConfirmEach));

    [Fact]
    public void AWidenedModeNeverApplies() =>
        Assert.Equal(AgentAccessMode.ConfirmEach, TalkActionPolicy.EffectiveMode(AgentAccessMode.ConfirmEach, AgentAccessMode.FullAccess));

    /// <summary>Every pair against an explicit narrow-to-wide ladder, so reordering AgentAccessMode fails here.</summary>
    [Fact]
    public void EffectiveModeIsTheNarrowerOfEveryPair()
    {
        AgentAccessMode[] ladder = [AgentAccessMode.Preview, AgentAccessMode.ConfirmEach, AgentAccessMode.Auto, AgentAccessMode.FullAccess];
        Assert.Equal(ladder, Enum.GetValues<AgentAccessMode>());
        for (var s = 0; s < ladder.Length; s++)
            for (var c = 0; c < ladder.Length; c++)
                Assert.Equal(ladder[Math.Min(s, c)], TalkActionPolicy.EffectiveMode(ladder[s], ladder[c]));
    }
}
