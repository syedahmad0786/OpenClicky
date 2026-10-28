using OpenClicky.Core;
using OpenClicky.Providers;

namespace OpenClicky.Core.Tests;

[Collection("OfflineEnv")]
public class OfflineAgentPlannerTests
{
    [Fact]
    public void IsEnvEnabled_FalseByDefault()
    {
        var previous = Environment.GetEnvironmentVariable(OfflineAgentPlanner.EnvVarName);
        try
        {
            Environment.SetEnvironmentVariable(OfflineAgentPlanner.EnvVarName, null);
            Assert.False(OfflineAgentPlanner.IsEnvEnabled());
        }
        finally
        {
            Environment.SetEnvironmentVariable(OfflineAgentPlanner.EnvVarName, previous);
        }
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("YES")]
    public void IsEnvEnabled_RecognizesTruthyValues(string value)
    {
        var previous = Environment.GetEnvironmentVariable(OfflineAgentPlanner.EnvVarName);
        try
        {
            Environment.SetEnvironmentVariable(OfflineAgentPlanner.EnvVarName, value);
            Assert.True(OfflineAgentPlanner.IsEnvEnabled());
        }
        finally
        {
            Environment.SetEnvironmentVariable(OfflineAgentPlanner.EnvVarName, previous);
        }
    }

    [Fact]
    public void CreateAgentPlanner_ReturnsOfflineWhenEnvSet_WithoutSecrets()
    {
        var previous = Environment.GetEnvironmentVariable(OfflineAgentPlanner.EnvVarName);
        try
        {
            Environment.SetEnvironmentVariable(OfflineAgentPlanner.EnvVarName, "1");
            var planner = ProviderFactory.CreateAgentPlanner(new AppSettings(), secrets: null);
            Assert.IsType<OfflineAgentPlanner>(planner);
        }
        finally
        {
            Environment.SetEnvironmentVariable(OfflineAgentPlanner.EnvVarName, previous);
        }
    }

    [Fact]
    public void CreateAgentPlanner_NullWhenEnvUnsetAndNoKey()
    {
        var previous = Environment.GetEnvironmentVariable(OfflineAgentPlanner.EnvVarName);
        try
        {
            Environment.SetEnvironmentVariable(OfflineAgentPlanner.EnvVarName, null);
            var planner = ProviderFactory.CreateAgentPlanner(new AppSettings(), secrets: null);
            Assert.Null(planner);
        }
        finally
        {
            Environment.SetEnvironmentVariable(OfflineAgentPlanner.EnvVarName, previous);
        }
    }

    [Fact]
    public async Task NextStepAsync_TypesOnlyTestTextAndRequiresVisualVerification()
    {
        var planner = new OfflineAgentPlanner();
        var prior = new List<string>();
        var tools = new List<string>();

        for (var i = 0; i < OfflineAgentPlanner.FixedPlan.Count; i++)
        {
            var step = await planner.NextStepAsync(
                "openclicky agent type hello in notepad",
                Array.Empty<ScreenCaptureResult>(),
                prior,
                "offline",
                CancellationToken.None);
            tools.Add(step.ToolName);
            Assert.Equal(OfflineAgentPlanner.FixedPlan[i].ToolName, step.ToolName);
            prior.Add($"step {i + 1}: {step.ToolName} → success=True ok");
        }

        Assert.Equal(["type_text", "done"], tools);

        var typeHello = OfflineAgentPlanner.FixedPlan[0];
        Assert.Equal(OfflineAgentPlanner.SafeTypeText, typeHello.Arguments["text"]);
        Assert.True(OfflineAgentPlanner.FixedPlan[^1].Done);
        Assert.Contains("Check the actual editor", OfflineAgentPlanner.FixedPlan[^1].Arguments["summary"]);

        var overflow = await planner.NextStepAsync(
            "again",
            Array.Empty<ScreenCaptureResult>(),
            prior,
            "offline",
            CancellationToken.None);
        Assert.True(overflow.Done);
        Assert.Equal("done", overflow.ToolName);
    }

    [Fact]
    public void FixedPlan_FirstStepRequiresApprovalForTestText()
    {
        var first = OfflineAgentPlanner.FixedPlan[0];
        Assert.Equal("type_text", first.ToolName);
        Assert.Equal("hello", first.Arguments["text"]);
        Assert.False(first.Done);
    }
}
