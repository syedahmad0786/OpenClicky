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
}
