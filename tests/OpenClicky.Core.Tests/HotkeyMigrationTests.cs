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

    private const string LegacyChord = """{"pushToTalk":{"win":false,"alt":true,"control":true,"shift":false,"virtualKey":81}""";

    private static void WithFile(string json, Action<string> test)
    {
        var path = Path.Combine(Path.GetTempPath(), $"openclicky-migrate-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        try { test(path); }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
            if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
        }
    }

    [Fact]
    public void LoadSavesTheMigration() => WithFile(LegacyChord + "}", path =>
    {
        var loaded = SettingsStore.Load(path);
        Assert.Equal(["Ctrl", "Alt", "Q"], loaded.TalkHotkey.Keys);
        var again = SettingsStore.Load(path); // from disk, already migrated
        Assert.Equal(HotkeyMigration.CurrentVersion, again.ShortcutsVersion);
        Assert.Equal(["Ctrl", "Alt", "Q"], again.TalkHotkey.Keys);
    });

    [Fact]
    public void LoadKeepsTheMigrationInMemoryWhenTheFileCannotBeWritten() => WithFile(LegacyChord + "}", path =>
    {
        File.SetAttributes(path, FileAttributes.ReadOnly);
        var loaded = SettingsStore.Load(path);
        Assert.Equal(["Ctrl", "Alt", "Q"], loaded.TalkHotkey.Keys);
        Assert.Equal(LegacyChord + "}", File.ReadAllText(path));
    });

    [Fact]
    public void LoadNeverOverwritesACorruptFile() => WithFile("{ not json", path =>
    {
        Assert.Equal(0, SettingsStore.Load(path).ShortcutsVersion);
        Assert.Equal("{ not json", File.ReadAllText(path));
    });

    [Fact]
    public void LoadLeavesAPlaintextKeyForTheSecretMigration() => WithFile(LegacyChord + ""","openRouterApiKey":"sk-test-plaintext"}""", path =>
    {
        var loaded = SettingsStore.Load(path);
        Assert.Equal("sk-test-plaintext", loaded.OpenRouterApiKey);
        Assert.Equal(["Ctrl", "Alt", "Q"], loaded.TalkHotkey.Keys); // migrated in memory; saved with the key's own migration
        Assert.Contains("sk-test-plaintext", File.ReadAllText(path)); // still on disk if Credential Manager fails
    });
}
