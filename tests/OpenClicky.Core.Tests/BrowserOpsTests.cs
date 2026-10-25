using System.Text.Json;
using OpenClicky.Core.Browser;

namespace OpenClicky.Core.Tests;

public sealed class BrowserOpsTests
{
    [Fact]
    public void TwentyFourOps()
    {
        Assert.Equal(22, BrowserOps.ModelOps.Count);
        Assert.Equal(24, BrowserOps.All.Count);
        Assert.Equal(["end", "sweep"], BrowserOps.All.Except(BrowserOps.ModelOps));
        Assert.Equal(["open", "click", "fill", "press", "scroll", "wait_for", "back", "upload"], BrowserOps.BatchOps);
    }

    [Theory]
    [InlineData("tabs", BrowserOpKind.Read)]
    [InlineData("snapshot", BrowserOpKind.Read)]
    [InlineData("extract", BrowserOpKind.Read)]
    [InlineData("screenshot", BrowserOpKind.Read)]
    [InlineData("scroll", BrowserOpKind.Read)]
    [InlineData("wait_for", BrowserOpKind.Read)]
    [InlineData("close", BrowserOpKind.Local)]
    [InlineData("keep", BrowserOpKind.Local)]
    [InlineData("open", BrowserOpKind.Act)]
    [InlineData("click", BrowserOpKind.Act)]
    [InlineData("fill", BrowserOpKind.Act)]
    [InlineData("type", BrowserOpKind.Act)]
    [InlineData("press", BrowserOpKind.Act)]
    [InlineData("back", BrowserOpKind.Act)]
    [InlineData("click_at", BrowserOpKind.Act)]
    [InlineData("hover_at", BrowserOpKind.Act)]
    [InlineData("drag_at", BrowserOpKind.Act)]
    [InlineData("download", BrowserOpKind.Act)]
    [InlineData("dialog", BrowserOpKind.Act)]
    [InlineData("batch", BrowserOpKind.Act)]
    [InlineData("javascript", BrowserOpKind.AlwaysAsk)]
    [InlineData("upload", BrowserOpKind.AlwaysAsk)]
    [InlineData("end", BrowserOpKind.Lifecycle)]
    [InlineData("sweep", BrowserOpKind.Lifecycle)]
    public void KindsMatchTheAccessTable(string op, BrowserOpKind kind)
    {
        Assert.Equal(kind, BrowserOps.Kind(op, Args(new { })));
        if (op == "screenshot") Assert.Equal(BrowserOpKind.Local, BrowserOps.Kind(op, Args(new { save = true })));
    }

    [Fact]
    public void BatchWithUploadIsAlwaysAsk()
    {
        var steps = new object[] { new { op = "click", @ref = "e1" }, new { op = "upload", @ref = "e2", paths = new[] { @"C:\a.txt" } } };
        Assert.Equal(BrowserOpKind.AlwaysAsk, BrowserOps.Kind("batch", Args(new { steps })));
        Assert.Equal(BrowserOpKind.Act, BrowserOps.Kind("batch", Args(new { steps = steps[..1] })));
    }

    public static TheoryData<string, string> BadArgs()
    {
        const string why = "\"rationale\":\"needed\"";
        var manySteps = string.Join(",", Enumerable.Repeat("{\"op\":\"back\"}", 21));
        return new()
        {
            { "open", "{\"url\":\"ftp://example.com/\"," + why + "}" },
            { "open", "{\"url\":\"https://user:pw@example.com/\"," + why + "}" },
            { "open", "{\"url\":\"https://example.com/" + new string('a', 2049 - 20) + "\"," + why + "}" },
            { "click", "{\"ref\":\"12\"," + why + "}" },
            { "press", "{\"key\":\"F5\"," + why + "}" },
            { "fill", "{\"ref\":\"e1\",\"text\":\"" + new string('a', 20_001) + "\"," + why + "}" },
            { "batch", "{\"steps\":[" + manySteps + "]," + why + "}" },
            { "wait_for", "{\"timeout\":21}" },
            { "snapshot", "{\"tabId\":0}" },
            { "batch", "{\"steps\":[{\"op\":\"javascript\",\"code\":\"1\"}]," + why + "}" },
            { "nope", "{}" },
            { "snapshot", "{\"tabId\":\"4\"}" },
            { "snapshot", "{\"tabId\":4.5}" },
            { "batch", "{\"steps\":[{\"op\":\"back\",\"tabId\":\"4\"}]," + why + "}" },
            { "click", "{\"ref\":\"e1\",\"rationale\":null}" },
        };
    }

    [Theory]
    [MemberData(nameof(BadArgs))]
    public void RejectsBadUrlRefKeyAndLimits(string op, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var problem = BrowserOps.Validate(op, doc.RootElement);
        Assert.False(string.IsNullOrWhiteSpace(problem));
    }

    [Fact]
    public void AcceptsGoodArgs()
    {
        Assert.Null(BrowserOps.Validate("open", Args(new { url = "https://example.com/a?b=1", rationale = "Open the docs" })));
        Assert.Null(BrowserOps.Validate("click", Args(new { tabId = 4, @ref = "e12", rationale = "Open the menu" })));
        Assert.Null(BrowserOps.Validate("snapshot", Args(new { tabId = 4, interactive = true })));
        Assert.Null(BrowserOps.Validate("wait_for", Args(new { text = "Done", timeout = 20 })));
        Assert.Null(BrowserOps.Validate("batch", Args(new
        {
            rationale = "Search",
            steps = new object[] { new { op = "fill", @ref = "e3", text = "cats" }, new { op = "press", key = "Enter" } },
        })));
    }

    [Fact]
    public void NullsAreAbsentAndWholeNumberTabIdsPass()
    {
        using var doc = JsonDocument.Parse("""
            {"op":"snapshot","tabId":null,"query":null,"say":null,"rationale":null,"interactive":null,"save":null}
            """);
        Assert.Null(BrowserOps.Validate("snapshot", doc.RootElement));
        Assert.Equal(BrowserOpKind.Read, BrowserOps.Kind("screenshot", JsonDocument.Parse("""{"save":null}""").RootElement));
        Assert.Null(BrowserOps.Validate("snapshot", JsonDocument.Parse("""{"tabId":4.0}""").RootElement));
        Assert.Null(BrowserOps.Validate("wait_for", JsonDocument.Parse("""{"timeout":null,"text":"Done"}""").RootElement));
        Assert.Null(BrowserOps.Validate("batch", JsonDocument.Parse("""
            {"rationale":"Go back","say":null,"steps":[{"op":"back","tabId":null},{"op":"click","ref":"e1","dismiss":null}]}
            """).RootElement));
        Assert.Null(BrowserOps.Validate("snapshot", Args(new { say = new string('a', 400) })));
    }

    [Fact]
    public void ActionsNeedRationale()
    {
        Assert.NotNull(BrowserOps.Validate("click", Args(new { @ref = "e1" })));
        Assert.NotNull(BrowserOps.Validate("javascript", Args(new { code = "1+1", rationale = " " })));
        Assert.NotNull(BrowserOps.Validate("click", Args(new { @ref = "e1", rationale = new string('a', 501) })));
        Assert.Null(BrowserOps.Validate("click", Args(new { @ref = "e1", rationale = "Open the menu" })));
        Assert.Null(BrowserOps.Validate("snapshot", Args(new { })));
        Assert.Null(BrowserOps.Validate("close", Args(new { tabId = 3 })));
    }

    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);
}
