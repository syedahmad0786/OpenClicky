using OpenClicky.Core.CompanionCursor;

namespace OpenClicky.Core.Tests;

public class CompanionStyleTests
{
    [Fact]
    public void FromSettings()
    {
        var defaults = CompanionStyle.From(new AppSettings(), false);
        Assert.Equal(CursorPresence.Following, defaults.Presence);
        Assert.Equal("#3380FF", defaults.AccentHex);
        Assert.True(defaults.ShakeToDetach);
        Assert.True(defaults.ScreenHighlight);
        Assert.False(defaults.ReducedMotion);

        Assert.Equal(CursorPresence.Off, CompanionStyle.From(new AppSettings { Paused = true, CursorHidden = true }, false).Presence);
        Assert.Equal(CursorPresence.Docked, CompanionStyle.From(new AppSettings { CursorHidden = true }, false).Presence);
        Assert.Equal("#3380FF", CompanionStyle.From(new AppSettings { AccentColor = "#123456" }, false).AccentHex);
        Assert.Equal("#FF4C4C", CompanionStyle.From(new AppSettings { AccentColor = "#ff4c4c" }, true).AccentHex);
        Assert.True(CompanionStyle.From(new AppSettings(), true).ReducedMotion);
    }
}
