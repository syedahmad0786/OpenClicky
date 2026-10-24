using OpenClicky.Core.Settings;
using OpenClicky.Core.Shell;

namespace OpenClicky.Core.Tests;

public class HotkeyMigrationTests
{
    [Fact]
    public void CustomPushToTalkMigratesToTalkHotkey()
    {
        var s = new AppSettings { PushToTalk = new HotkeyChord(Win: false, Alt: true, Control: true, Shift: false, VirtualKey: 0x51) }; // Ctrl+Alt+Q

        Assert.True(HotkeyMigration.Apply(s));

        Assert.True(s.TalkHotkey.SameChord(new HotkeyBinding(["Ctrl", "Alt", "Q"], HotkeyTrigger.Hold)));
        Assert.Equal(2, s.ShortcutsVersion);
        Assert.False(HotkeyMigration.Apply(s)); // runs once
    }

    [Fact]
    public void DefaultChordKeepsTheNewDefaultTalkHotkey()
    {
        var s = new AppSettings();
        Assert.Equal(0, s.ShortcutsVersion);

        Assert.True(HotkeyMigration.Apply(s));

        Assert.True(s.TalkHotkey.SameChord(HotkeyBinding.DefaultTalk));
        Assert.Equal(2, s.ShortcutsVersion);
    }

    [Fact]
    public void AlreadyMigratedSettingsAreLeftAlone()
    {
        var custom = new HotkeyBinding(["Shift", "F9"], HotkeyTrigger.Hold);
        var s = new AppSettings { PushToTalk = new HotkeyChord(false, true, true, false, 0x51), TalkHotkey = custom, ShortcutsVersion = 2 };

        Assert.False(HotkeyMigration.Apply(s));

        Assert.Same(custom, s.TalkHotkey);
    }

    [Fact]
    public void ChordWithAKeyShortcutsCannotUseKeepsTheDefault()
    {
        var s = new AppSettings { PushToTalk = new HotkeyChord(false, false, true, false, 0x0D) }; // Ctrl+Enter

        Assert.True(HotkeyMigration.Apply(s));

        Assert.True(s.TalkHotkey.SameChord(HotkeyBinding.DefaultTalk));
        Assert.Equal(2, s.ShortcutsVersion);
    }
}
