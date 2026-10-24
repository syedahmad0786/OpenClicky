namespace OpenClicky.Core.Tests;

public class CompanionPromptsTests
{
    [Fact]
    public void TalkPromptStaysWithinBudget()
    {
        var prompt = CompanionPrompts.TalkSystemPrompt;
        Assert.True(prompt.Length <= 12_000, $"talk prompt is {prompt.Length} characters");
        Assert.Contains("[DRAW:", prompt);
        Assert.Contains("start of, or inside, the sentence it illustrates", prompt);
        Assert.DoesNotContain("[MARK:", prompt); // still accepted, no longer taught
        Assert.DoesNotContain("green", prompt);
        foreach (var op in Enum.GetValues<OpenClicky.Core.Drawing.DrawOpKind>().Where(k => k != OpenClicky.Core.Drawing.DrawOpKind.Figure))
            Assert.Contains(op.ToString().ToLowerInvariant(), prompt);
    }
}
