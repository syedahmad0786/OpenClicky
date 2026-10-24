using System.Text.Json;
using OpenClicky.Core.Settings;
using OpenClicky.Core.Shell;

namespace OpenClicky.Core.Tests;

public class SettingsPatchTests
{
    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static string Snapshot(AppSettings s) => JsonSerializer.Serialize(s);

    private static PatchResult Apply(AppSettings s, string key, string json, out string? error, bool confirmed = false) =>
        SettingsPatch.TryApply(s, key, J(json), out error, confirmed);

    [Theory]
    [InlineData("nope.unknown", "true")]
    [InlineData("voice.captions", "\"yes\"")]
    [InlineData("voice.speed", "\"fast\"")]
    [InlineData("voice.speed", "2")]
    [InlineData("voice.speed", "0.6")]
    [InlineData("voice.voice", "\"\"")]
    [InlineData("voice.voice", "42")]
    [InlineData("dictation.language", "\"xx\"")]
    [InlineData("dictation.dictionary", "\"word\"")]
    [InlineData("dictation.dictionary", "[1, 2]")]
    [InlineData("cursor.colour", "\"#000000\"")]
    [InlineData("cursor.colour", "7")]
    [InlineData("access.mode", "\"root\"")]
    [InlineData("access.denyList", "[]")]
    [InlineData("advanced.proxyUrl", "\"http://evil.example.com:8787\"")]
    [InlineData("advanced.proxyUrl", "\"http://localhost.evil.com\"")]
    [InlineData("advanced.proxyUrl", "\"ftp://127.0.0.1\"")]
    [InlineData("advanced.proxyUrl", "\"http://user:pw@127.0.0.1:8787\"")]
    [InlineData("advanced.proxyUrl", "\"\"")]
    [InlineData("account.provider", "\"bing\"")]
    [InlineData("account.model", "\"\"")]
    [InlineData("account.model", "\"bad model!\"")]
    [InlineData("shortcuts.talk", "{\"keys\":[\"Foo\"],\"trigger\":\"hold\"}")]
    [InlineData("shortcuts.talk", "{\"keys\":[],\"trigger\":\"hold\"}")]
    [InlineData("shortcuts.talk", "{\"keys\":[\"Ctrl\"],\"trigger\":\"tap\"}")]
    [InlineData("shortcuts.talk", "{\"keys\":[\"A\"],\"trigger\":\"hold\"}")]
    [InlineData("shortcuts.talk", "{\"keys\":[\"Ctrl\",\"A\",\"B\"],\"trigger\":\"hold\"}")]
    [InlineData("shortcuts.talk", "{\"keys\":[\"Ctrl\",\"Ctrl\"],\"trigger\":\"hold\"}")]
    [InlineData("shortcuts.talk", "{\"keys\":[\"Ctrl\",\"A\"],\"trigger\":\"doubleTap\"}")]
    [InlineData("shortcuts.talk", "\"Ctrl+Alt\"")]
    public void RejectsUnknownKeyWrongTypeAndRange(string key, string json)
    {
        var s = new AppSettings();
        var before = Snapshot(s);
        Assert.Equal(PatchResult.Rejected, Apply(s, key, json, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Equal(before, Snapshot(s));
    }

    [Fact]
    public void AppliesEveryKeyWithAValidValue()
    {
        var s = new AppSettings();
        var cases = new (string Key, string Json)[]
        {
            ("voice.voice", "\"alloy\""), ("voice.speed", "1.25"), ("voice.captions", "false"), ("voice.readReplies", "false"),
            ("voice.outputDeviceId", "\"spk-1\""), ("microphone.deviceId", "\"mic-1\""),
            ("dictation.autoDetect", "false"), ("dictation.language", "\"fr\""), ("dictation.dictionary", "[\"Clicky\"]"),
            ("shortcuts.talk", "{\"keys\":[\"Ctrl\",\"Shift\"],\"trigger\":\"hold\"}"),
            ("shortcuts.text", "{\"keys\":[\"F9\"],\"trigger\":\"doubleTap\"}"),
            ("shortcuts.dictate", "{\"keys\":[\"Alt\",\"D\"],\"trigger\":\"hold\"}"),
            ("shortcuts.handsFree", "{\"keys\":[\"Ctrl\",\"Alt\"],\"trigger\":\"doubleTap\"}"),
            ("cursor.colour", "\"#26c281\""), ("cursor.hidden", "true"), ("cursor.screenHighlight", "false"), ("cursor.shakeToDetach", "false"),
            ("agents.speakAnnouncements", "false"), ("agents.cursorUpdates", "false"),
            ("access.mode", "\"preview\""), ("access.denyList", "\"bank, vault\""), ("access.rememberHistory", "false"),
            ("advanced.useLocalProxy", "true"), ("advanced.proxyUrl", "\"http://localhost:9000/\""), ("advanced.forceGdiCapture", "true"),
            ("account.provider", "\"openrouter\""), ("account.model", "\"gpt-5\""), ("account.quickModel", "\"\""),
            ("account.openRouterModel", "\"openai/gpt-4o-mini\""),
        };
        foreach (var (key, json) in cases)
            Assert.True(PatchResult.Applied == Apply(s, key, json, out var error), $"{key}: {error}");

        Assert.Equal("alloy", s.VoiceId);
        Assert.Equal(1.25, s.SpeechSpeed);
        Assert.False(s.Captions);
        Assert.False(s.SpeakReplies);
        Assert.Equal("spk-1", s.OutputDeviceId);
        Assert.Equal("mic-1", s.MicrophoneDeviceId);
        Assert.Equal("fr", s.DictationLanguage);
        Assert.Equal(["Clicky"], s.DictionaryWords);
        Assert.Equal(["Ctrl", "Shift"], s.TalkHotkey.Keys);
        Assert.Equal(HotkeyTrigger.DoubleTap, s.TextHotkey.Trigger);
        Assert.Equal("#26C281", s.AccentColor);
        Assert.Equal(AgentAccessMode.Preview, s.AgentAccess);
        Assert.Equal("bank, vault", s.AgentDenyListExtra);
        Assert.Equal("http://localhost:9000", s.LocalProxyBaseUrl);
        Assert.Equal(AuthProvider.OpenRouter, s.AuthProvider);
        Assert.Equal("", s.ChatGptQuickModel);
    }

    [Fact]
    public void RejectsConflictingHotkeys()
    {
        var s = new AppSettings();
        var before = Snapshot(s);

        // Talk defaults to hold Ctrl+Alt: same keys in another order and the same trigger conflict.
        Assert.Equal(PatchResult.Rejected, Apply(s, "shortcuts.text", "{\"keys\":[\"alt\",\"control\"],\"trigger\":\"hold\"}", out var error));
        Assert.Contains("Talk", error);
        Assert.Equal(before, Snapshot(s));

        // Dictate defaults to hold Ctrl+Win; hands-free is double-tap on the same keys, so that pair is not a conflict.
        Assert.Equal(PatchResult.Rejected, Apply(s, "shortcuts.handsFree", "{\"keys\":[\"Win\",\"Ctrl\"],\"trigger\":\"hold\"}", out error));
        Assert.Contains("Dictate", error);
        Assert.Equal(PatchResult.Applied, Apply(s, "shortcuts.talk", "{\"keys\":[\"Ctrl\",\"Alt\"],\"trigger\":\"doubleTap\"}", out error));
        Assert.Equal(PatchResult.Applied, Apply(s, "shortcuts.dictate", "{\"keys\":[\"Ctrl\",\"Win\"],\"trigger\":\"hold\"}", out error));
    }

    [Fact]
    public void ReSavingAnActionsOwnChordIsNotAConflict()
    {
        var s = new AppSettings();
        Assert.Equal(PatchResult.Applied, Apply(s, "shortcuts.talk", "{\"keys\":[\"Alt\",\"Ctrl\"],\"trigger\":\"hold\"}", out _));
        Assert.Equal(["Ctrl", "Alt"], s.TalkHotkey.Keys);
    }

    [Fact]
    public void CapsDictionaryAndDenyList()
    {
        var s = new AppSettings();
        var words = Enumerable.Range(0, 400).Select(i => $"\"Word{i}\"").Prepend("\"  clicky \"").Prepend("\"CLICKY\"").Prepend("\"\"")
            .Append("\"" + new string('x', 500) + "\"");
        Assert.Equal(PatchResult.Applied, Apply(s, "dictation.dictionary", "[" + string.Join(",", words) + "]", out _));
        Assert.Equal(50, s.DictionaryWords.Count);
        Assert.Equal("CLICKY", s.DictionaryWords[0]);
        Assert.Single(s.DictionaryWords, w => w.Equals("clicky", StringComparison.OrdinalIgnoreCase));
        Assert.All(s.DictionaryWords, w => Assert.InRange(w.Length, 1, 40));

        var longWord = "[\"" + new string('y', 500) + "\"]";
        Assert.Equal(PatchResult.Applied, Apply(s, "dictation.dictionary", longWord, out _));
        Assert.Equal(new string('y', 40), Assert.Single(s.DictionaryWords));

        Assert.Equal(PatchResult.Applied, Apply(s, "access.denyList", "\"" + new string('d', 5_000) + "\"", out _));
        Assert.Equal(2_000, s.AgentDenyListExtra!.Length);
        Assert.Equal(PatchResult.Applied, Apply(s, "access.denyList", "\"   \"", out _));
        Assert.Null(s.AgentDenyListExtra);
    }

    [Fact]
    public void AutoAndFullAccessRequireConfirmationAndDoNotChangeUntilConfirmed()
    {
        var s = new AppSettings();
        Assert.Equal(PatchResult.RequiresConfirmation, Apply(s, "access.mode", "\"full\"", out var error));
        Assert.Null(error);
        Assert.Equal(AgentAccessMode.ConfirmEach, s.AgentAccess);
        Assert.Equal(PatchResult.RequiresConfirmation, Apply(s, "access.mode", "\"auto\"", out _));
        Assert.Equal(AgentAccessMode.ConfirmEach, s.AgentAccess);

        Assert.Equal(PatchResult.Applied, Apply(s, "access.mode", "\"full\"", out _, confirmed: true));
        Assert.Equal(AgentAccessMode.FullAccess, s.AgentAccess);
        // Already in that mode: nothing to warn about. Stepping down never needs a warning.
        Assert.Equal(PatchResult.Applied, Apply(s, "access.mode", "\"full\"", out _));
        Assert.Equal(PatchResult.Applied, Apply(s, "access.mode", "\"preview\"", out _));
        Assert.Equal(AgentAccessMode.Preview, s.AgentAccess);
    }

    [Theory]
    [InlineData("http://127.0.0.1:8787", "http://127.0.0.1:8787")]
    [InlineData("https://localhost:9443/v1/", "https://localhost:9443/v1")]
    [InlineData("http://[::1]:8080", "http://[::1]:8080")]
    public void ProxyUrlAcceptsLoopbackOnly(string input, string stored)
    {
        var s = new AppSettings();
        Assert.Equal(PatchResult.Applied, SettingsPatch.TryApply(s, "advanced.proxyUrl", JsonDocument.Parse(JsonSerializer.Serialize(input)).RootElement, out _));
        Assert.Equal(stored, s.LocalProxyBaseUrl);
    }

    [Fact]
    public void NormalizeRepairsHandEditedSettings()
    {
        var s = new AppSettings
        {
            SpeechSpeed = 9,
            TalkHotkey = new HotkeyBinding(["Nonsense"], HotkeyTrigger.Hold),
            DictionaryWords = [.. Enumerable.Range(0, 99).Select(i => "w" + i)],
            DictationLanguage = "zz",
            AgentDenyListExtra = new string('a', 9_000),
        };
        SettingsPatch.Normalize(s);
        Assert.Equal(1, s.SpeechSpeed);
        Assert.Equal(["Ctrl", "Alt"], s.TalkHotkey.Keys);
        Assert.Equal(50, s.DictionaryWords.Count);
        Assert.Equal("en", s.DictationLanguage);
        Assert.Equal(2_000, s.AgentDenyListExtra!.Length);
    }
}
