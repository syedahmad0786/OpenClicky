using OpenClicky.Core;

namespace OpenClicky.Core.Tests;

public class TalkActionPolicyTests
{
    [Theory]
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
}
