namespace OpenClicky.Core.Tests;

public class ConversationHistoryStoreTests
{
    [Fact]
    public void RoundTrip_KeepsLastTurnsOnly()
    {
        var path = Path.Combine(Path.GetTempPath(), $"openclicky-history-{Guid.NewGuid():N}.json");
        try
        {
            var turns = Enumerable.Range(1, 12)
                .Select(i => ($"user {i}", $"assistant {i}"))
                .ToList();
            ConversationHistoryStore.Save(turns, path, maxTurns: 10);
            var loaded = ConversationHistoryStore.Load(path, maxTurns: 10);
            Assert.Equal(10, loaded.Count);
            Assert.Equal("user 3", loaded[0].user);
            Assert.Equal("assistant 12", loaded[^1].assistant);
            Assert.DoesNotContain("screenshot", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            ConversationHistoryStore.Clear(path);
        }
    }
}
