using OpenClicky.Core.Settings;
using OpenClicky.Core.Speech;

namespace OpenClicky.Core.Tests;

public class VoiceCatalogTests
{
    [Fact]
    public void ListsTheTenOpenAiVoicesWithDescriptors()
    {
        Assert.Equal(["alloy", "ash", "ballad", "cedar", "coral", "echo", "marin", "sage", "shimmer", "verse"], VoiceCatalog.OpenAiIds);
        Assert.Equal(VoiceCatalog.OpenAiIds, SettingsCatalog.OpenAiVoices);
        Assert.All(VoiceCatalog.OpenAi, v => Assert.InRange(v.Descriptor.Length, 3, 40));
        Assert.Equal(VoiceCatalog.OpenAi.Count, VoiceCatalog.OpenAi.Select(v => v.Descriptor).Distinct().Count());
    }

    [Theory]
    [InlineData("cedar", true)]
    [InlineData("Cedar", false)]
    [InlineData("windows-default", false)]
    [InlineData("win:0123456789abcdef", false)]
    [InlineData(null, false)]
    public void RecognisesOpenAiVoiceIds(string? id, bool expected) => Assert.Equal(expected, VoiceCatalog.IsOpenAi(id));

    [Fact]
    public void ViewDescribesOpenAiVoicesOnceAKeyIsSaved()
    {
        var view = ShellSettingsBuilder.Build(new AppSettings(), new SettingsRuntime { OpenAiKeySet = true });
        var marin = Assert.Single(view.Voice.Voices, v => v.Id == "marin");
        Assert.True(marin.Available);
        Assert.Equal(VoiceCatalog.Descriptor("marin"), marin.Detail);
    }
}
