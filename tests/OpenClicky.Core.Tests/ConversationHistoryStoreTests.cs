namespace OpenClicky.Core.Tests;

public class ConversationHistoryTurnsTests
{
    [Fact]
    public void LoadTurnsReturnsNewestWithTimesOldestFirst()
    {
        var path = Path.Combine(Path.GetTempPath(), "oc-turns-" + Guid.NewGuid().ToString("N"), "history.json");
        for (var i = 1; i <= 3; i++) ConversationHistoryStore.Append("u" + i, "a" + i, path);
        var turns = ConversationHistoryStore.LoadTurns(path, maxTurns: 2);
        Assert.Equal(["u2", "u3"], turns.Select(t => t.User));
        Assert.True(turns[0].Id < turns[1].Id);
        Assert.True(turns[1].CreatedAtUtc > DateTimeOffset.UtcNow.AddMinutes(-5));
    }
}

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
            Assert.DoesNotContain(loaded, turn => turn.user.Contains("screenshot", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            ConversationHistoryStore.Clear(path);
        }
    }

    [Fact]
    public void RepeatedIdenticalTurnsAreRetained()
    {
        var path = Path.Combine(Path.GetTempPath(), $"openclicky-history-{Guid.NewGuid():N}.json");
        ConversationHistoryStore.Save(new[] { ("same", "same") }, path);
        ConversationHistoryStore.Save(new[] { ("same", "same") }, path);

        Assert.Equal(2, ConversationHistoryStore.Load(path, 100).Count);
    }

    [Fact]
    public void Append_ReturnsDurableSourceIdAndFindsExactTurn()
    {
        var path = Path.Combine(Path.GetTempPath(), $"openclicky-history-{Guid.NewGuid():N}.json");
        try
        {
            var first = ConversationHistoryStore.Append("first user", "first reply", path);
            var second = ConversationHistoryStore.Append("second user", "second reply", path);

            Assert.True(second.Id > first.Id);
            Assert.Equal(first, ConversationHistoryStore.Find(first.Id, path));
            Assert.Null(ConversationHistoryStore.Find(0, path));
        }
        finally { ConversationHistoryStore.Clear(path); }
    }

    [Fact]
    public void Clear_RemovesConversationDerivedMemoryAndTombstonesItsSource()
    {
        var path = Path.Combine(Path.GetTempPath(), $"openclicky-shared-{Guid.NewGuid():N}.json");
        try
        {
            var turn = ConversationHistoryStore.Append("I prefer brief replies.", "Okay.", path);
            Assert.Single(AutomaticMemory.LearnFromTurn(turn, path));

            ConversationHistoryStore.Clear(path);

            Assert.Empty(ConversationHistoryStore.Load(path));
            Assert.Empty(CompanionMemoryStore.Load(path).Memory!.Facts);
            Assert.True(CompanionMemoryStore.IsSourceDeleted("conversation", turn.Id.ToString(), path));
        }
        finally
        {
            ConversationHistoryStore.Clear(path, path);
            CompanionMemoryStore.Clear(path);
        }
    }

    [Fact]
    public void CorruptSqliteStore_IsReportedInsteadOfLookingLikeEmptyHistory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"openclicky-history-{Guid.NewGuid():N}.json");
        var database = Path.ChangeExtension(path, ".db");
        File.WriteAllText(database, "not a sqlite database");
        try
        {
            Assert.ThrowsAny<Microsoft.Data.Sqlite.SqliteException>(() => ConversationHistoryStore.Load(path));
        }
        finally
        {
            foreach (var file in new[] { database, database + "-wal", database + "-shm" })
                if (File.Exists(file)) File.Delete(file);
        }
    }
}
