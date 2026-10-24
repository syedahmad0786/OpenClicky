using OpenClicky.Core;

namespace OpenClicky.Core.Tests;

public class TaskVoiceCommandsTests
{
    [Fact]
    public void SelfContainedVisualDoesNotRequireTargetSwitchUnlessAgentSelected()
    {
        const string request = "Create an interactive calculator with Research 3 and Build 5";
        Assert.False(HomeRequestRouting.RequiresTargetSwitch(request, agentSelected: false));
        Assert.True(HomeRequestRouting.RequiresTargetSwitch(request, agentSelected: true));
        Assert.True(HomeRequestRouting.RequiresTargetSwitch("Create a chart from this screenshot", agentSelected: false));
    }
    [Theory]
    [InlineData("pause task")]
    [InlineData("note: private words")]
    [InlineData("remember this: blue is preferred")]
    public void HomeAgentModeCannotSwallowExplicitLocalCommand(string text)
    {
        var route = HomeRequestRouting.Resolve(text, agentSelected: true);
        Assert.True(route.IsLocal);
        Assert.Equal(text, route.Request);
        Assert.DoesNotContain("openclicky agent", route.Request, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Stop task.", TaskVoiceCommandKind.Stop, "")]
    [InlineData("Resume task!", TaskVoiceCommandKind.Resume, "")]
    [InlineData("Take a note, check the inbox tomorrow.", TaskVoiceCommandKind.Note, "check the inbox tomorrow.")]
    [InlineData("note: Private draft", TaskVoiceCommandKind.Note, "Private draft")]
    [InlineData("Remember this: I prefer short replies", TaskVoiceCommandKind.Remember, "I prefer short replies")]
    [InlineData("Change task, draft only", TaskVoiceCommandKind.ChangeTask, "draft only")]
    [InlineData("Does that note look right?", TaskVoiceCommandKind.None, "")]
    [InlineData("Remember what I said?", TaskVoiceCommandKind.None, "")]
    [InlineData("Stop task and send the email", TaskVoiceCommandKind.None, "")]
    public void RecognizesOnlyExplicitCommands(string text, TaskVoiceCommandKind expected, string content)
    { var result = TaskVoiceCommands.Parse(text); Assert.Equal(expected, result.Kind); Assert.Equal(content, result.Text); }
}
