using OpenClicky.Core;

namespace OpenClicky.Core.Tests;

public class HotkeySettingsPersistTests
{
    [Fact]
    public void SaveLoad_RoundTripsRemappedHotkey()
    {
        var path = Path.Combine(Path.GetTempPath(), $"openclicky-hotkey-{Guid.NewGuid():N}.json");
        try
        {
            var remapped = new HotkeyChord(Win: false, Alt: true, Control: true, Shift: false, VirtualKey: 0x51); // Alt+Ctrl+Q
            var settings = new AppSettings { PushToTalk = remapped };

            SettingsStore.Save(settings, path);
            var json = File.ReadAllText(path);
            Assert.Contains("\"pushToTalk\"", json, StringComparison.Ordinal);
            Assert.Contains("\"virtualKey\": 81", json, StringComparison.Ordinal);

            var loaded = SettingsStore.Load(path);
            Assert.Equal(remapped, loaded.PushToTalk);
            Assert.Equal("Ctrl+Alt+Q", loaded.PushToTalk.DisplayName);
            Assert.True(loaded.PushToTalk.IsValid);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Load_ReplacesInvalidHotkeyWithDefault()
    {
        var path = Path.Combine(Path.GetTempPath(), $"openclicky-hotkey-bad-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(
                path,
                """
                {
                  "pushToTalk": {
                    "win": false,
                    "alt": false,
                    "control": false,
                    "shift": false,
                    "virtualKey": 0
                  }
                }
                """);

            var loaded = SettingsStore.Load(path);
            Assert.Equal(HotkeyChord.Default, loaded.PushToTalk);
            Assert.Equal("Win+Alt+Space", loaded.PushToTalk.DisplayName);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Save_NormalizesInvalidHotkeyBeforePersist()
    {
        var path = Path.Combine(Path.GetTempPath(), $"openclicky-hotkey-norm-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new AppSettings
            {
                PushToTalk = new HotkeyChord(false, false, false, false, 0x41),
            };

            SettingsStore.Save(settings, path);
            Assert.Equal(HotkeyChord.Default, settings.PushToTalk);

            var loaded = SettingsStore.Load(path);
            Assert.Equal(HotkeyChord.Default, loaded.PushToTalk);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
