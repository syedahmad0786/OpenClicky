using OpenClicky.Core;

namespace OpenClicky.Core.Tests;

public class ScreenGuidanceTests
{
    [Fact]
    public void WindowContext_ClipsAndScalesIntoEachMonitorImage()
    {
        var context = AppKnowledge.WindowContext((-100, 20, 200, 80), Screens);
        Assert.Contains("screen1: x=0, y=10, width=50, height=40", context);
        Assert.Contains("screen2: x=50, y=10, width=50, height=40", context);
        Assert.Equal("", AppKnowledge.WindowContext(null, Screens));
        Assert.Equal("", AppKnowledge.WindowContext((1000, 1000, 20, 20), Screens));
    }

    private static readonly ScreenCaptureResult[] Screens =
    [new([], 100, 100, 0, 0, 200, 200, true, "first"), new([], 100, 100, -200, 0, 200, 200, false, "second")];

    [Fact]
    public void Marks_LeaveSpeechAndPointTagIntact_AndMapSecondaryMonitor()
    {
        var (text, marks) = ScreenAnnotations.Parse("Here is Projects. [MARK:circle:10,20,30,40:Projects:screen2] [POINT:25,40:Projects:screen2]");
        Assert.DoesNotContain("MARK", text);
        Assert.Equal("Here is Projects.", PointParser.Parse(text).SpokenText);
        Assert.Equal(new VirtualAnnotation("circle", -180, 40, 60, 80, "Projects"), Assert.Single(ScreenAnnotations.Map(marks, Screens)));
    }

    [Theory]
    [InlineData("[MARK:circle:95,20,30,40:bad]")]
    [InlineData("[MARK:circle:10,20,0,40:bad]")]
    [InlineData("[MARK:arrow:10,20,10,20:bad]")]
    [InlineData("[MARK:circle:10,20,30,40:bad:screen3]")]
    [InlineData("[MARK:circle:2147483647,20,30,40:bad]")]
    [InlineData("[MARK:circle:9999999999999999999,20,30,40:bad]")]
    public void InvalidMarks_AreNotDrawnOrSpoken(string tag)
    {
        var parsed = ScreenAnnotations.Parse("answer " + tag);
        Assert.Equal("answer", parsed.Text);
        Assert.Empty(ScreenAnnotations.Map(parsed.Marks, Screens));
    }

    [Fact]
    public void Arrow_MapsEndpoints_AndMarksAreBounded()
    {
        var parsed = ScreenAnnotations.Parse(string.Join(" ", Enumerable.Repeat("[MARK:arrow:10,20,80,90:next]", 20)));
        Assert.Equal(6, parsed.Marks.Count);
        Assert.Equal(new VirtualAnnotation("arrow", 20, 40, 160, 180, "next"), ScreenAnnotations.Map(parsed.Marks, Screens)[0]);
    }

    [Fact]
    public void PolygonFreehandAndText_MapEveryPointToTheSelectedMonitor()
    {
        var parsed = ScreenAnnotations.Parse(
            "look [MARK:polygon:10,10,50,10,30,40:area:screen2] [MARK:freehand:0,0,25,50,99,99:path] [MARK:text:50,25:save here:screen2]");

        Assert.Equal("look", parsed.Text);
        var mapped = ScreenAnnotations.Map(parsed.Marks, Screens);
        Assert.Equal(new[] { new AnnotationPoint(-180, 20), new(-100, 20), new(-140, 80) }, mapped[0].Points);
        Assert.Equal(new[] { new AnnotationPoint(0, 0), new(50, 100), new(198, 198) }, mapped[1].Points);
        Assert.Equal(new VirtualAnnotation("text", -100, 50, 0, 0, "save here"), mapped[2]);
    }

    [Theory]
    [InlineData("[MARK:polygon:1,1,2,2:too few]")]
    [InlineData("[MARK:polygon:1,1,2,2,3:odd]")]
    [InlineData("[MARK:freehand:1,1:too few]")]
    [InlineData("[MARK:freehand:1,1,1,1:duplicate]")]
    [InlineData("[MARK:freehand:1,1,2,2,3,101:outside]")]
    [InlineData("[MARK:text:1,2,3:wrong coords]")]
    [InlineData("[MARK:text:1,2::screen1]")]
    [InlineData("[MARK:text:1,2:abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcdefghi]")]
    public void NewMarks_FailClosedAndNeverLeakRawTags(string tag)
    {
        var parsed = ScreenAnnotations.Parse("spoken " + tag);

        Assert.Equal("spoken", parsed.Text);
        Assert.Empty(ScreenAnnotations.Map(parsed.Marks, Screens));
    }

    [Fact]
    public void UnterminatedMarkTag_IsRemovedFromSpokenText()
    {
        var parsed = ScreenAnnotations.Parse("safe words [MARK:polygon:1,1,2,2,3,3:raw payload");

        Assert.Equal("safe words", parsed.Text);
        Assert.Empty(parsed.Marks);
    }

    [Fact]
    public void CurvesMapAllFourPointsAndRejectWrongOrOutOfBoundsControls()
    {
        var valid = ScreenAnnotations.Parse("curve [MARK:curve:10,10,20,50,70,50,90,10:arc:screen2]");
        Assert.Equal("curve", valid.Text);
        Assert.Equal(new AnnotationPoint[] { new(-180, 20), new(-160, 100), new(-60, 100), new(-20, 20) },
            Assert.Single(ScreenAnnotations.Map(valid.Marks, Screens)).Points);
        Assert.Empty(ScreenAnnotations.Parse("[MARK:curve:1,1,2,2,3,3:bad]").Marks);
        Assert.Empty(ScreenAnnotations.Map(ScreenAnnotations.Parse("[MARK:curve:1,1,2,2,300,3,4,4:bad]").Marks, Screens));
    }

    [Fact]
    public void MultipleMalformedMarkTags_DoNotRemoveInterveningSpeech()
    {
        var parsed = ScreenAnnotations.Parse("Say [MARK:broken] then useful prose [MARK:bad");

        Assert.Contains("Say", parsed.Text);
        Assert.Contains("then useful prose", parsed.Text);
        Assert.DoesNotContain("MARK:", parsed.Text);
        Assert.Empty(parsed.Marks);
    }

    [Fact]
    public void AppGuide_UsesCurrentAppAndDoesNotTreatPreviousAppAsCurrent()
    {
        var context = AppKnowledge.Context("Claude", "ChatGPT");
        Assert.Contains("Current app hint: Claude", context);
        Assert.Contains("Previous app hint this session: ChatGPT", context);
        Assert.Contains("support.claude.com", context);
        Assert.Contains("Never reuse a target", context);
        Assert.Equal("", AppKnowledge.Identify("a document mentioning Claude"));
        Assert.Equal("ChatGPT", AppKnowledge.Identify("ChatGPT - Google Chrome"));
        Assert.Equal("Claude", AppKnowledge.Identify("Claude - Google Chrome"));
    }

    [Fact]
    public void AppSwitch_RequiresRepeatedActivity_AndWaitsFiveMinutesBetweenOffers()
    {
        var guide = new AppSwitchGuide();
        var now = DateTimeOffset.UtcNow;
        Assert.Null(guide.Observe("ChatGPT", true, now));
        Assert.Null(guide.Observe("ChatGPT", true, now.AddSeconds(4)));
        for (var i = 1; i <= 5; i++) Assert.Null(guide.Observe(i % 2 == 0 ? "ChatGPT" : "Claude", true, now.AddSeconds(i * 5)));
        Assert.Null(guide.Observe("Claude", false, now.AddSeconds(28)));
        Assert.Equal("Claude", guide.Observe("Claude", true, now.AddSeconds(29)));
        for (var i = 0; i < 8; i++) Assert.Null(guide.Observe(i % 2 == 0 ? "ChatGPT" : "Claude", true, now.AddSeconds(40 + i * 3)));
        Assert.Null(guide.Observe("Claude", true, now.AddSeconds(70)));
        for (var i = 0; i < 6; i++) guide.Observe(i % 2 == 0 ? "ChatGPT" : "Claude", true, now.AddSeconds(330 + i * 3));
        Assert.Equal("Claude", guide.Observe("Claude", true, now.AddSeconds(349)));
        guide.Snooze(now.AddSeconds(350));
        for (var i = 0; i < 6; i++) guide.Observe(i % 2 == 0 ? "ChatGPT" : "Claude", true, now.AddSeconds(1000 + i * 3));
        Assert.Null(guide.Observe("Claude", true, now.AddSeconds(1020)));
    }

    [Fact]
    public void AppSwitch_ExcludedOrMissingWindowClearsPendingActivity()
    {
        var guide = new AppSwitchGuide();
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 6; i++) guide.Observe(i % 2 == 0 ? "A" : "B", true, now.AddSeconds(i));
        Assert.Null(guide.Observe(null, true, now.AddSeconds(7)));
        Assert.Null(guide.Observe("B", true, now.AddSeconds(10)));
        Assert.Null(guide.Observe("B", true, now.AddSeconds(20)));
    }

    [Fact]
    public void QuickModel_DoesNotChangeAgentModel()
    {
        var settings = new AppSettings { ChatGptModel = "agent-model", ChatGptQuickModel = "fast-model" };
        Assert.Equal("fast-model", settings.ResolveTalkModel());
        Assert.Equal("agent-model", settings.ResolveVisionModel());
    }
}
