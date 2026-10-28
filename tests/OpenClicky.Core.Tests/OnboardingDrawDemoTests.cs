using OpenClicky.Core;
using OpenClicky.Core.Drawing;
using OpenClicky.Core.Onboarding;

namespace OpenClicky.Core.Tests;

public class OnboardingDrawDemoTests
{
    private static ScreenCaptureResult Screen(int w, int h, bool cursor = true) =>
        new([0xff, 0xd8], w, h, 0, 0, w, h, cursor, "screen");

    [Fact]
    public void ReplyParsesToThreeShapesAndSpokenText()
    {
        var reply = OnboardingDrawDemo.Reply([Screen(1920, 1080)]);
        var (text, ops) = DrawCommandParser.Parse(reply);

        Assert.Equal([DrawOpKind.Circle, DrawOpKind.Arrow, DrawOpKind.Sticky], ops.Select(o => o.Kind));
        Assert.Equal(["demo-circle", "demo-arrow", "demo-note"], ops.Select(o => o.Id));
        var point = PointParser.Parse(text);
        Assert.True(point.IsNone);
        Assert.Equal(OnboardingDrawDemo.Spoken, point.SpokenText);
    }

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    [InlineData(3840, 2160)]
    [InlineData(1080, 1920)]
    public void AllPointsInsideScreenshot(int w, int h)
    {
        var ops = DrawCommandParser.Parse(OnboardingDrawDemo.Reply([Screen(w, h)])).Ops;

        Assert.Equal(3, ops.Count);
        foreach (var op in ops)
        {
            Assert.All(op.Points, p => { Assert.InRange(p.X, 0, w); Assert.InRange(p.Y, 0, h); });
            Assert.InRange(op.Box.X, 0, w);
            Assert.InRange(op.Box.Y, 0, h);
            Assert.InRange(op.Box.X + op.Box.W, 0, w);
            Assert.InRange(op.Box.Y + op.Box.H, 0, h);
        }
        Assert.True(ops[0].Box.W > 0);
    }

    [Fact]
    public void SecondScreenUsesScreenIndex()
    {
        var reply = OnboardingDrawDemo.Reply([Screen(1920, 1080, cursor: false), Screen(1280, 720)]);
        var ops = DrawCommandParser.Parse(reply).Ops;

        Assert.Equal(3, ops.Count);
        Assert.All(ops, o => Assert.Equal(2, o.Screen));
        Assert.Equal(640, ops[0].Points[0].X);
    }

    [Fact]
    public void FirstScreenOmitsScreenIndex()
    {
        var ops = DrawCommandParser.Parse(OnboardingDrawDemo.Reply([Screen(1920, 1080)])).Ops;

        Assert.All(ops, o => Assert.Null(o.Screen));
    }

    [Fact]
    public void NoScreensFallsBack()
    {
        var ops = DrawCommandParser.Parse(OnboardingDrawDemo.Reply([])).Ops;

        Assert.Equal(3, ops.Count);
        Assert.Equal(960, ops[0].Points[0].X);
        Assert.Equal(540, ops[0].Points[0].Y);
        Assert.Equal(108, ops[0].Box.W / 2);
    }

    [Fact]
    public async Task ClientReturnsReply()
    {
        var screens = new[] { Screen(1920, 1080) };
        var client = new OnboardingDrawTalkClient();

        var reply = await client.CompleteVisionTalkAsync("", [], OnboardingDrawDemo.Prompt, screens, "m");

        Assert.Equal(OnboardingDrawDemo.Reply(screens), reply);
    }

    /// <summary>The walkthrough triggers are inline in TalkOrchestrator.RunTurnAsync: "show me how" / "guide me", ignoring case.</summary>
    [Fact]
    public void RequestRoutesAsAPlainTalkTurn()
    {
        var request = OnboardingDrawDemo.Request;

        Assert.False(request.StartsWith("show me how", StringComparison.OrdinalIgnoreCase));
        Assert.False(request.StartsWith("guide me", StringComparison.OrdinalIgnoreCase));
        Assert.False(AgentIntent.TryExtract(request, out _));
        Assert.False(VisualArtifactReply.IsSelfContainedRequest(request));
        Assert.StartsWith("show me how", OnboardingDrawDemo.Prompt, StringComparison.OrdinalIgnoreCase); // why Prompt is display-only
    }
}
