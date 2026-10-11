using OpenClicky.Core;

namespace OpenClicky.Core.Tests;

public class HotkeyChordTests
{
    [Fact]
    public void Default_IsWinAltSpace()
    {
        var chord = HotkeyChord.Default;
        Assert.True(chord.Win);
        Assert.True(chord.Alt);
        Assert.False(chord.Control);
        Assert.Equal(0x20, chord.VirtualKey);
        Assert.Equal("Win+Alt+Space", chord.DisplayName);
    }

    [Fact]
    public void Matches_RequiresExactModifiers()
    {
        var chord = HotkeyChord.Default;
        Assert.True(chord.Matches(winDown: true, altDown: true, controlDown: false, shiftDown: false, virtualKey: 0x20));
        Assert.False(chord.Matches(winDown: true, altDown: true, controlDown: true, shiftDown: false, virtualKey: 0x20));
    }
}
