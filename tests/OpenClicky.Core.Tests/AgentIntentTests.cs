using OpenClicky.Core;

namespace OpenClicky.Core.Tests;

public class AgentIntentTests
{
    [Theory]
    [InlineData("openclicky agent open notepad", "open notepad")]
    [InlineData("heyclicky agent clean my desktop", "clean my desktop")]
    [InlineData("agent: type hello", "type hello")]
    [InlineData("open Slack", "open Slack")]
    [InlineData("Okay, I want you to open Slack for me.", "Okay, I want you to open Slack for me.")]
    [InlineData("Can you open Notepad?", "Can you open Notepad?")]
    [InlineData("Please launch Notepad for me.", "Please launch Notepad for me.")]
    [InlineData("Find that chat about Windows", "Find that chat about Windows")]
    [InlineData("Could you search for the conversation about the campaign?", "Could you search for the conversation about the campaign?")]
    [InlineData("Open Calculator. Then search local memory for DEMO-20261012 and report the matching conversation with its source kind and record ID. Do not perform any other desktop actions.", "Open Calculator. Then search local memory for DEMO-20261012 and report the matching conversation with its source kind and record ID. Do not perform any other desktop actions.")]
    [InlineData("Please launch Notepad, then type hello in the document.", "Please launch Notepad, then type hello in the document.")]
    public void TryExtract_RecognizesPrefixes(string input, string expectedTask)
    {
        Assert.True(AgentIntent.TryExtract(input, out var task));
        Assert.Equal(expectedTask, task);
    }

    [Theory]
    [InlineData("what is on my screen?")]
    [InlineData("how do I open Slack?")]
    [InlineData("How do I find that chat?")]
    [InlineData("open Slack and send a message")]
    [InlineData("open https://example.com")]
    [InlineData("Don't open Calculator. Then search local memory.")]
    [InlineData("Tell me how to open Calculator. Then explain local memory search.")]
    [InlineData("\"Open Calculator. Then search local memory.\"")]
    public void TryExtract_NormalTalk_False(string text)
    {
        Assert.False(AgentIntent.TryExtract(text, out _));
    }
}
