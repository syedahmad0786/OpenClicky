using OpenClicky.Providers;

namespace OpenClicky.Core.Tests;

public class AgentPlannerParseTests
{
    [Fact]
    public void ParseStep_Click()
    {
        var step = OpenRouterAgentPlanner.ParseStep(
            """{"tool":"click","args":{"x":"10","y":"20"},"rationale":"hit start"}""");
        Assert.Equal("click", step.ToolName);
        Assert.Equal("10", step.Arguments["x"]);
        Assert.Equal("20", step.Arguments["y"]);
        Assert.False(step.Done);
    }

    [Fact]
    public void ParseStep_DoneWithFence()
    {
        var step = OpenRouterAgentPlanner.ParseStep(
            """
            ```json
            {"tool":"done","args":{"summary":"opened notepad"},"rationale":"finished"}
            ```
            """);
        Assert.True(step.Done);
        Assert.Equal("opened notepad", step.Arguments["summary"]);
    }

    [Fact]
    public void ParseStep_UiaSnapshot()
    {
        var step = OpenRouterAgentPlanner.ParseStep(
            """{"tool":"uia_snapshot","args":{},"rationale":"read controls"}""");
        Assert.Equal("uia_snapshot", step.ToolName);
        Assert.False(step.Done);
    }

    [Fact]
    public void ParseStep_ClickVirtualSpace()
    {
        var step = OpenRouterAgentPlanner.ParseStep(
            """{"tool":"click","args":{"x":"100","y":"200","space":"virtual"},"rationale":"uia center"}""");
        Assert.Equal("virtual", step.Arguments["space"]);
        Assert.Equal("100", step.Arguments["x"]);
    }

    [Fact]
    public void ParseStep_HotkeyAndScroll()
    {
        var hotkey = OpenRouterAgentPlanner.ParseStep(
            """{"tool":"hotkey","args":{"keys":"ctrl+n"},"rationale":"new"}""");
        Assert.Equal("hotkey", hotkey.ToolName);
        Assert.Equal("ctrl+n", hotkey.Arguments["keys"]);

        var scroll = OpenRouterAgentPlanner.ParseStep(
            """{"tool":"scroll","args":{"clicks":"-3"},"rationale":"down"}""");
        Assert.Equal("scroll", scroll.ToolName);
        Assert.Equal("-3", scroll.Arguments["clicks"]);
    }
}
