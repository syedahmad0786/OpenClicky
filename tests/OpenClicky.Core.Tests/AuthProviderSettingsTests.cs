namespace OpenClicky.Core.Tests;

public class AuthProviderSettingsTests
{
    [Fact]
    public void ResolveVisionModel_UsesChatGptWhenSelected()
    {
        var settings = new AppSettings
        {
            AuthProvider = AuthProvider.ChatGPT,
            ChatGptModel = "gpt-4o",
            OpenRouterModel = "openai/gpt-4o-mini",
        };
        Assert.Equal("gpt-4o", settings.ResolveVisionModel());
    }

    [Fact]
    public void ResolveVisionModel_UsesOpenRouterByDefault()
    {
        var settings = new AppSettings { OpenRouterModel = "openai/gpt-4o" };
        Assert.Equal(AuthProvider.OpenRouter, settings.AuthProvider);
        Assert.Equal("openai/gpt-4o", settings.ResolveVisionModel());
    }

    [Fact]
    public void Save_PersistsAuthProviderWithoutSecrets()
    {
        var path = Path.Combine(Path.GetTempPath(), $"openclicky-auth-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new AppSettings
            {
                AuthProvider = AuthProvider.ChatGPT,
                OpenRouterApiKey = "sk-should-strip",
                ChatGptAccountEmail = "user@example.com",
                ChatGptHostId = "urn:uuid:11111111-1111-1111-1111-111111111111",
                ChatGptIssuedClientId = "oaiapp_test",
            };
            SettingsStore.Save(settings, path);
            var json = File.ReadAllText(path);
            Assert.DoesNotContain("sk-should-strip", json);
            Assert.Contains("chatGPT", json, StringComparison.OrdinalIgnoreCase);

            var loaded = SettingsStore.Load(path);
            Assert.Equal(AuthProvider.ChatGPT, loaded.AuthProvider);
            Assert.Null(loaded.OpenRouterApiKey);
            Assert.Equal("user@example.com", loaded.ChatGptAccountEmail);
            Assert.Equal("oaiapp_test", loaded.ChatGptIssuedClientId);
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
