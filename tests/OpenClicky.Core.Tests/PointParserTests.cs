using OpenClicky.Core;

namespace OpenClicky.Core.Tests;

public class PointParserTests
{
    [Fact]
    public void Parse_NoneTag_StripsAndFlagsNone()
    {
        var result = PointParser.Parse("look at the toolbar. [POINT:none]");
        Assert.Equal("look at the toolbar.", result.SpokenText);
        Assert.True(result.IsNone);
        Assert.Null(result.X);
    }

    [Fact]
    public void Parse_CoordinatesWithLabel()
    {
        var result = PointParser.Parse("click save up there [POINT:1100,42:save button]");
        Assert.Equal("click save up there", result.SpokenText);
        Assert.Equal(1100, result.X);
        Assert.Equal(42, result.Y);
        Assert.Equal("save button", result.ElementLabel);
        Assert.Null(result.ScreenNumber);
        Assert.False(result.IsNone);
    }

    [Fact]
    public void Parse_ScreenNumber()
    {
        var result = PointParser.Parse("other monitor [POINT:400,300:terminal:screen2]");
        Assert.Equal(400, result.X);
        Assert.Equal(300, result.Y);
        Assert.Equal("terminal", result.ElementLabel);
        Assert.Equal(2, result.ScreenNumber);
    }

    [Fact]
    public void Parse_NoTag_ReturnsFullText()
    {
        var result = PointParser.Parse("just chatting");
        Assert.Equal("just chatting", result.SpokenText);
        Assert.False(result.IsNone);
        Assert.Null(result.X);
    }
}
