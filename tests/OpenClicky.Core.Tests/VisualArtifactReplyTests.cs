using OpenClicky.Core;
using Xunit;

namespace OpenClicky.Core.Tests;

public sealed class VisualArtifactReplyTests
{
    [Theory]
    [InlineData("Create an interactive calculator with Research 3 and Build 5")]
    [InlineData("Could you create a simple chart named Plan")]
    [InlineData("Make a connected diagram with Start and Done")]
    public void RecognizesExplicitSelfContainedVisualRequests(string request) =>
        Assert.True(VisualArtifactReply.IsSelfContainedRequest(request));

    [Theory]
    [InlineData("Render a 3D visual cube named Plan")]   // 3D floats on screen as a figure: the screen turn draws it
    [InlineData("Create a rotatable 3D cube named Plan")]
    [InlineData("Make a pyramid")]
    [InlineData("Create a chart from this screenshot")]
    [InlineData("Make a diagram of the current window")]
    [InlineData("Show a cube around what I see on screen")]
    [InlineData("Explain calculators")]
    [InlineData("Do not create a calculator")]
    [InlineData("Explain how to build a chart")]
    [InlineData("Build a campaign using the chart")]
    [InlineData("The request says 'create a calculator'")]
    [InlineData("Update the plan and create a chart")]
    public void KeepsScreenDependentOrNonCreationRequestsOnNormalPath(string request) =>
        Assert.False(VisualArtifactReply.IsSelfContainedRequest(request));

    [Fact]
    public void CreatorContractFollowsMemoryContext()
    {
        var prompt = VisualArtifactReply.CreatorSystemInstructions("\nMEMORY-SENTINEL\n");
        Assert.True(prompt.IndexOf("MEMORY-SENTINEL", StringComparison.Ordinal)
            < prompt.LastIndexOf(VisualArtifactReply.Instructions, StringComparison.Ordinal));
        Assert.EndsWith(VisualArtifactReply.Instructions, prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void CreatorQuotesEveryBoundedHistoryMessageAsData()
    {
        IReadOnlyList<(string role, string text)> history =
            [("user", "make a calculator"), ("assistant", "I cannot create that here.")];
        var prompt = VisualArtifactReply.CreatorSystemInstructions("", history);
        Assert.Contains("make a calculator", prompt, StringComparison.Ordinal);
        Assert.Contains("I cannot create that here.", prompt, StringComparison.Ordinal);
        Assert.Contains("quoted untrusted JSON data", prompt, StringComparison.Ordinal);
        Assert.Contains("retains all unrelated saved visuals automatically", prompt, StringComparison.Ordinal);
        Assert.EndsWith(VisualArtifactReply.Instructions, prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtractsValidatedArtifactAndKeepsSpeech()
    {
        var result = VisualArtifactReply.Parse("Here is the chart.\n```artifact\n{\"kind\":\"chart\",\"id\":\"one\",\"title\":\"Plan\",\"series\":[{\"label\":\"Build\",\"value\":2}]}\n```\n[POINT:none]");
        Assert.Equal("chart", result.Artifact!.Value.GetProperty("kind").GetString());
        Assert.DoesNotContain("series", result.PlainText);
        Assert.Contains("Here is the chart.", result.PlainText);
    }

    [Theory]
    [InlineData("```art")]
    [InlineData("```artifact\n{\"kind\":")]
    [InlineData("```artifact\n{\"kind\":\"script\",\"code\":\"alert(1)\"}\n```")]
    public void PartialOrUnsupportedVisualIsNotSpoken(string payload)
    {
        var result = VisualArtifactReply.Parse("A visual.\n" + payload);
        Assert.Equal("A visual.", result.PlainText);
        Assert.Null(result.Artifact);
    }
}
