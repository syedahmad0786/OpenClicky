using OpenClicky.Core;

namespace OpenClicky.Core.Tests;

public class AgentIntentTests
{
    [Theory]
    [InlineData("openclicky agent open notepad", "open notepad")]
    [InlineData("heyclicky agent clean my desktop", "clean my desktop")]
    [InlineData("agent: type hello", "type hello")]
    public void TryExtract_RecognizesPrefixes(string input, string expectedTask)
    {
        Assert.True(AgentIntent.TryExtract(input, out var task));
        Assert.Equal(expectedTask, task);
    }

    [Fact]
    public void TryExtract_NormalTalk_False()
    {
        Assert.False(AgentIntent.TryExtract("what is on my screen?", out _));
    }
}
