using System.Text.Json;
using OpenClicky.Core;

namespace OpenClicky.Core.Tests;

public class VisualArtifactStoreTests
{
    [Fact]
    public void Save_ReopensExactSourceAndPreservesFollowupId()
    {
        var path = TemporaryPath();
        try { using var json = JsonDocument.Parse("""{"kind":"chart","id":"capacity","title":"Capacity","followupId":"turn-42","series":[{"label":"Build","value":3}]}"""); VisualArtifactStore.Save("session-a", "conversation-a", json.RootElement, path); var saved = Assert.Single(VisualArtifactStore.Load("session-a", path)); Assert.Equal("turn-42", saved.FollowupId); Assert.Equal(json.RootElement.GetRawText(), saved.SourceJson); Assert.Empty(VisualArtifactStore.Load("session-b", path)); }
        finally { File.Delete(path); File.Delete(Path.ChangeExtension(path, ".db")); }
    }

    [Theory]
    [InlineData("{\"kind\":\"chart\",\"id\":\"x\",\"title\":\"X\",\"series\":[],\"html\":\"<script>\"}")]
    [InlineData("{\"kind\":\"diagram\",\"id\":\"x\",\"title\":\"X\",\"nodes\":[{\"id\":\"a\",\"label\":\"A\",\"detail\":\"\"}],\"edges\":[{\"from\":\"a\",\"to\":\"missing\"}]}")]
    [InlineData("{\"kind\":\"scene3d\",\"id\":\"x\",\"title\":\"X\",\"shape\":\"cube\",\"color\":\"url(js)\",\"rotation\":0}")]
    public void Parse_RejectsExecutableUnknownAndBrokenContent(string raw)
    {
        using var json = JsonDocument.Parse(raw);
        Assert.Throws<InvalidDataException>(() => VisualArtifactStore.Parse("session", "conversation", json.RootElement));
    }

    private static string TemporaryPath() => Path.Combine(Path.GetTempPath(), $"openclicky-artifacts-{Guid.NewGuid():N}.json");
}
