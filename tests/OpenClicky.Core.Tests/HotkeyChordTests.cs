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
        Assert.True(chord.IsValid);
    }

    [Fact]
    public void Matches_RequiresExactModifiers()
    {
        var chord = HotkeyChord.Default;
        Assert.True(chord.Matches(winDown: true, altDown: true, controlDown: false, shiftDown: false, virtualKey: 0x20));
        Assert.False(chord.Matches(winDown: true, altDown: true, controlDown: true, shiftDown: false, virtualKey: 0x20));
    }

    [Fact]
    public void TryValidate_RejectsEmpty()
    {
        var empty = new HotkeyChord(false, false, false, false, 0);
        Assert.False(empty.TryValidate(out var error));
        Assert.Contains("empty", error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("(not set)", empty.DisplayName);
    }

    [Fact]
    public void TryValidate_RejectsBareKeyWithoutModifier()
    {
        var bare = new HotkeyChord(false, false, false, false, 0x41); // A
        Assert.False(bare.TryValidate(out var error));
        Assert.Contains("modifier", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryValidate_RejectsModifierOnlyVirtualKey()
    {
        var modOnly = new HotkeyChord(true, false, false, false, 0x5B); // LWin as key
        Assert.False(modOnly.TryValidate(out var error));
        Assert.Contains("modifier", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryValidate_AcceptsCtrlShiftLetter()
    {
        var chord = new HotkeyChord(false, false, true, true, 0x54); // Ctrl+Shift+T
        Assert.True(chord.TryValidate(out var error));
        Assert.Null(error);
        Assert.Equal("Ctrl+Shift+T", chord.DisplayName);
    }

    [Theory]
    [InlineData(0x70, "F1")]
    [InlineData(0x78, "F9")]
    [InlineData(0x7B, "F12")]
    [InlineData(0x87, "F24")]
    public void DisplayName_FormatsWindowsFunctionKeys(int key, string name)
    {
        var chord = new HotkeyChord(false, false, true, true, key);

        Assert.Equal($"Ctrl+Shift+{name}", chord.DisplayName);
    }

    [Fact]
    public void DisplayName_UnknownKeyKeepsVirtualKeyFallback()
    {
        var chord = new HotkeyChord(false, false, true, false, 0x88);

        Assert.Equal("Ctrl+Vk88", chord.DisplayName);
    }

    [Theory]
    [InlineData(0x1B)]
    [InlineData(-1)]
    [InlineData(0xFF)]
    public void TryValidate_RejectsCancelOrInvalidKey(int key)
    {
        var chord = new HotkeyChord(false, false, true, true, key);
        Assert.False(chord.TryValidate(out var error));
        Assert.NotNull(error);
        Assert.Equal(HotkeyChord.Default, HotkeyChord.NormalizeOrDefault(chord));
    }

    [Fact]
    public void NormalizeOrDefault_FallsBackForInvalid()
    {
        Assert.Equal(HotkeyChord.Default, HotkeyChord.NormalizeOrDefault(null));
        Assert.Equal(HotkeyChord.Default, HotkeyChord.NormalizeOrDefault(new HotkeyChord(false, false, false, false, 0)));
        var valid = new HotkeyChord(false, true, true, false, 0x20);
        Assert.Equal(valid, HotkeyChord.NormalizeOrDefault(valid));
    }
}
