using System.Reflection;
using System.Text.Json;

namespace OpenClicky.Core.Tests;

public class SettingsStoreSecretTests
{
    [Fact]
    public void Save_ToAReadOnlySettingsFileLeavesNoTemporaryCopy()
    {
        var path = Path.Combine(Path.GetTempPath(), $"openclicky-settings-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{}");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            Assert.ThrowsAny<Exception>(() => SettingsStore.Save(new AppSettings(), path));
            Assert.False(File.Exists(path + ".tmp"));
            Assert.Equal("{}", File.ReadAllText(path));
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
    }

    [Fact]
    public void Save_StripsApiKeyFromDisk()
    {
        var path = Path.Combine(Path.GetTempPath(), $"openclicky-settings-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new AppSettings
            {
                OpenRouterApiKey = "sk-test-should-not-persist",
                OpenRouterModel = "openai/gpt-4o-mini",
                UseLocalProxy = true,
                UseWindowsSpeech = false,
                SpeakReplies = false,
                MicrophoneDeviceId = "test-capture-endpoint",
            };

            SettingsStore.Save(settings, path);
            var json = File.ReadAllText(path);
            Assert.DoesNotContain("sk-test-should-not-persist", json);

            var loaded = SettingsStore.Load(path);
            Assert.Null(loaded.OpenRouterApiKey);
            Assert.True(loaded.UseLocalProxy);
            Assert.False(loaded.UseWindowsSpeech);
            Assert.False(loaded.SpeakReplies);
            Assert.Equal("test-capture-endpoint", loaded.MicrophoneDeviceId);
            Assert.False(File.Exists(path + ".tmp"));
            Assert.Equal("openai/gpt-4o-mini", loaded.OpenRouterModel);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    // Values that pass load-time normalisation; anything else gets a generic non-default value.
    private static readonly Dictionary<string, object?> ValidOverrides = new()
    {
        ["AccentColor"] = "#26C281",
        ["DictationLanguage"] = "fr",
        ["SpeechSpeed"] = 1.25,
        ["LocalProxyBaseUrl"] = "http://127.0.0.1:9999",
        ["ShortcutsVersion"] = 3, // already migrated: Load must not move the custom PushToTalk onto TalkHotkey
        ["Proactive"] = new OpenClicky.Core.Proactive.ProactiveSettings { Suggestions = true, CalendarConnector = "work_cal" }, // test-only value; the default stays all off
    };

    private static IEnumerable<PropertyInfo> PersistedProperties() =>
        typeof(AppSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite);

    private static object? NonDefault(PropertyInfo p, AppSettings defaults)
    {
        if (ValidOverrides.TryGetValue(p.Name, out var v)) return v;
        var current = p.GetValue(defaults);
        var t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
        if (t == typeof(bool)) return !(bool)current!;
        if (t == typeof(string)) return "value-" + p.Name;
        if (t == typeof(int)) return (int)(current ?? 0) + 1;
        if (t == typeof(double)) return (double)(current ?? 0) + 0.25;
        if (t.IsEnum) return Enum.GetValues(t).Cast<object>().First(e => !e.Equals(current));
        if (t == typeof(List<string>)) return new List<string> { "alpha" };
        if (t == typeof(HotkeyChord)) return new HotkeyChord(Win: false, Alt: false, Control: true, Shift: true, VirtualKey: 0x41);
        // Complex type: borrow a differing default from another property of the same type.
        var other = PersistedProperties().Where(o => o.PropertyType == p.PropertyType && o.Name != p.Name)
            .Select(o => o.GetValue(defaults))
            .FirstOrDefault(o => Json(o) != Json(current));
        return other ?? throw new InvalidOperationException($"Add a non-default test value for {p.Name} ({t.Name}).");
    }

    private static string Json(object? value) => JsonSerializer.Serialize(value);

    [Fact]
    public void SaveLoad_RoundTripsEveryNonSecretProperty()
    {
        var path = Path.Combine(Path.GetTempPath(), $"openclicky-settings-{Guid.NewGuid():N}.json");
        try
        {
            var defaults = new AppSettings();
            var settings = new AppSettings();
            var props = PersistedProperties().Where(p => !SettingsStore.SecretProperties.Contains(p.Name)).ToList();
            foreach (var p in props)
            {
                var value = NonDefault(p, defaults);
                Assert.NotEqual(Json(p.GetValue(defaults)), Json(value));
                p.SetValue(settings, value);
            }

            SettingsStore.Save(settings, path);
            var loaded = SettingsStore.Load(path);

            foreach (var p in props)
            {
                Assert.True(Json(p.GetValue(settings)) == Json(p.GetValue(loaded)), $"{p.Name} did not survive Save/Load");
            }
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Save_NeverWritesSecretProperties()
    {
        Assert.Equal(["OpenRouterApiKey"], SettingsStore.SecretProperties);
        // A new secret-looking field must be added to SecretProperties (and nulled in CloneWithoutSecrets).
        var looksSecret = PersistedProperties().Select(p => p.Name)
            .Where(n => System.Text.RegularExpressions.Regex.IsMatch(n, "ApiKey|Token|Secret|Password", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        Assert.Equal(SettingsStore.SecretProperties, looksSecret);
        var path = Path.Combine(Path.GetTempPath(), $"openclicky-settings-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new AppSettings();
            foreach (var name in SettingsStore.SecretProperties)
            {
                typeof(AppSettings).GetProperty(name)!.SetValue(settings, "secret-" + name);
            }

            SettingsStore.Save(settings, path);
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            foreach (var name in SettingsStore.SecretProperties)
            {
                Assert.DoesNotContain("secret-" + name, json);
                Assert.DoesNotContain(doc.RootElement.EnumerateObject(), e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
                Assert.Equal("secret-" + name, typeof(AppSettings).GetProperty(name)!.GetValue(settings)); // caller's copy untouched
            }
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
