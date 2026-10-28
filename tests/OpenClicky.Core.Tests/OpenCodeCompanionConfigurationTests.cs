using System.Text.Json;
using OpenClicky.Providers;

namespace OpenClicky.Core.Tests;

public class OpenCodeCompanionConfigurationTests
{
    [Fact]
    public void ProfileUsesOnlyOwnedProviderAndBrokerTools()
    {
        var profile = new OpenCodeCompanionConfiguration(new("http://127.0.0.1:31234/"), "ephemeral-inference",
            new("http://127.0.0.1:31235/mcp"), "ephemeral-tools", "gpt-6-luna");
        using var doc = JsonDocument.Parse(profile.ToConfigJson());
        var root = doc.RootElement;
        Assert.Equal("deny", root.GetProperty("permission").GetProperty("*").GetString());
        Assert.Equal("allow", root.GetProperty("permission").GetProperty("openclicky_*").GetString());
        // The only engine tool beyond the broker is the session checklist, which touches nothing outside the session.
        Assert.Equal("allow", root.GetProperty("permission").GetProperty("todowrite").GetString());
        Assert.Equal(3, root.GetProperty("permission").EnumerateObject().Count());
        Assert.Contains("Use only the openclicky tools, plus todowrite for your checklist.", root.GetProperty("agent").GetProperty("companion").GetProperty("prompt").GetString());
        Assert.Single(root.GetProperty("enabled_providers").EnumerateArray());
        Assert.Equal("http://127.0.0.1:31234/v1", root.GetProperty("provider").GetProperty(OpenCodeCompanionConfiguration.ProviderId)
            .GetProperty("options").GetProperty("baseURL").GetString());
        Assert.False(root.GetProperty("mcp").GetProperty("openclicky").GetProperty("oauth").GetBoolean());
        Assert.DoesNotContain("ephemeral", profile.ToString());
    }

    [Fact]
    public void PromptAsksForTheResultBlockOnlyWhenTheRunDoes()
    {
        var profile = new OpenCodeCompanionConfiguration(new("http://127.0.0.1:31234/"), "a", new("http://127.0.0.1:31235/mcp"), "b", "gpt-6-luna");
        using var doc = JsonDocument.Parse(profile.ToConfigJson());
        var prompt = doc.RootElement.GetProperty("agent").GetProperty("companion").GetProperty("prompt").GetString()!;
        Assert.Contains(OpenClicky.Core.Work.JobResult.FenceTag, prompt);
        Assert.Contains("exactly one", prompt);
    }

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("http://127.0.0.1:4321/?secret=x")]
    [InlineData("http://user@127.0.0.1:4321/")]
    public void RejectsNonOwnedEndpoint(string endpoint) => Assert.Throws<ArgumentException>(() =>
        new OpenCodeCompanionConfiguration(new(endpoint), "token", new("http://127.0.0.1:1234/mcp"), "token", "gpt-6-luna"));
}
