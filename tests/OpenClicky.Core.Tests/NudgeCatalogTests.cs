using OpenClicky.Core.Proactive;
using OpenClicky.Core.Shell;

namespace OpenClicky.Core.Tests;

public class NudgeCatalogTests
{
    [Fact]
    public void TwentyAppsUniqueIds()
    {
        Assert.Equal(20, NudgeCatalog.Apps.Count);
        Assert.Equal(20, NudgeCatalog.Apps.Select(a => a.Id).Distinct().Count());
    }

    [Theory]
    [InlineData("slack", "anything", "slack")]
    [InlineData("Slack", null, "slack")]
    [InlineData("chrome", "Inbox (3) - me@example.com - Gmail", "gmail")]
    [InlineData("msedge", "Q3 plan - Google Docs - Personal - Microsoft Edge", "googledocs")]
    [InlineData("firefox", "Board | Linear", "linear")]
    [InlineData("notepad", "Gmail notes - Notepad", null)]
    [InlineData("chrome", "Slack notes for Monday", null)]
    [InlineData(null, null, null)]
    [InlineData("chrome", null, null)]
    [InlineData(null, "Gmail", null)]
    public void MatchesDesktopProcessAndBrowserTitle(string? process, string? title, string? expectedId) =>
        Assert.Equal(expectedId, NudgeCatalog.Match(process, title)?.Id);

    [Fact]
    public void NudgeCardCopy()
    {
        var card = ProactiveCards.Nudge(NudgeCatalog.Apps.Single(a => a.Id == "notion"));
        Assert.Equal("proactive-nudge-notion", card.Id);
        Assert.Equal(NotchCardKind.Tip, card.Kind);
        Assert.Equal("Connect Notion?", card.Title);
        Assert.Equal("You've spent a while in Notion. Connect it in Integrations so I can help with it.", card.Body);
        Assert.Equal(18000, card.AutoDismissMs);
        Assert.Equal([("connect", "Connect", CardActionStyle.Primary), ("notnow", "Not now", CardActionStyle.Secondary),
            ("never", "Never ask", CardActionStyle.Secondary)], card.Actions.Select(a => (a.Id, a.Label, a.Style)));
    }

    [Fact]
    public void ConnectedByNameOrHost()
    {
        var notion = NudgeCatalog.Apps.Single(a => a.Id == "notion");
        Assert.True(NudgeCatalog.IsConnected(notion, [new("k", "My Notion", "mcp.example.com")]));
        Assert.True(NudgeCatalog.IsConnected(notion, [new("k", "Tools", "mcp.notion.com")]));
        Assert.False(NudgeCatalog.IsConnected(notion, [new("k", "Tools", "mcp.example.com")]));
        Assert.False(NudgeCatalog.IsConnected(notion, []));
    }
}
