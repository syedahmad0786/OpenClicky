using OpenClicky.Core.Settings;

namespace OpenClicky.Core.Tests;

public class SettingsCatalogTests
{
    [Fact]
    public void FiftyLanguagesWithNativeNames()
    {
        var languages = SettingsCatalog.Languages;
        Assert.Equal(50, languages.Count);
        Assert.Equal(50, languages.Select(l => l.Code).Distinct().Count());
        Assert.All(languages, l =>
        {
            Assert.False(string.IsNullOrWhiteSpace(l.Name));
            Assert.False(string.IsNullOrWhiteSpace(l.Native));
        });
        Assert.Equal("en", languages[0].Code);
    }
}
