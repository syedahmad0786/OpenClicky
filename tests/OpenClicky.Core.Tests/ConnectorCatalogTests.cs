namespace OpenClicky.Core.Tests;

public class ConnectorCatalogTests
{
    [Fact]
    public void IdsAreUniqueAndLowerCase()
    {
        var ids = ConnectorCatalog.Apps.Select(a => a.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(ids, id => Assert.Equal(id.ToLowerInvariant(), id));
        Assert.Equal(17, ids.Count);
        Assert.Equal("linear", ids[0]);
        Assert.Equal("obsidian", ids[^1]);
    }

    [Fact]
    public void ServerAppsHaveAValidHttpsUrl()
    {
        foreach (var app in ConnectorCatalog.Apps.Where(a => a.Setup is CatalogSetup.SignIn or CatalogSetup.ApiKey or CatalogSetup.None))
        {
            Assert.StartsWith("https://", app.Url);
            Assert.NotNull(ConnectorRegistry.ValidateUrl(app.Url!));
        }
    }

    [Fact]
    public void ComposioTakesYourUrlAndObsidianIsAFolder()
    {
        var composio = ConnectorCatalog.Find("composio")!;
        Assert.Equal(CatalogSetup.Url, composio.Setup);
        Assert.Equal("x-api-key", composio.ApiKeyHeader);
        Assert.Null(composio.Url);
        var obsidian = ConnectorCatalog.Find("obsidian")!;
        Assert.Equal(CatalogSetup.Folder, obsidian.Setup);
        Assert.Equal("github", ConnectorCatalog.Find("github")!.Id);
        Assert.Equal("Authorization", ConnectorCatalog.Find("github")!.ApiKeyHeader);
        Assert.Equal(CatalogSetup.ApiKey, ConnectorCatalog.Find("github")!.Setup);
    }

    [Fact]
    public void MultiAccountIsExactlySignInAndApiKey()
    {
        foreach (var app in ConnectorCatalog.Apps)
            Assert.Equal(app.Setup is CatalogSetup.SignIn or CatalogSetup.ApiKey, app.MultiAccount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nope")]
    [InlineData("Linear")]
    public void FindReturnsNullForUnknownIds(string? id) => Assert.Null(ConnectorCatalog.Find(id));
}
