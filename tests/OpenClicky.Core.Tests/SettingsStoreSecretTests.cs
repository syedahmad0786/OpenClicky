namespace OpenClicky.Core.Tests;

public class SettingsStoreSecretTests
{
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
}
