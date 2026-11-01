using System.Text;
using OpenClicky.Core;

namespace OpenClicky.Core.Tests;

public class CompanionMemoryStoreTests
{
    [Fact]
    public void ProfileAndFacts_CanBeEditedDeletedAndCleared()
    {
        var path = TemporaryPath();
        try
        {
            var profile = CompanionMemoryStore.SetProfile(
                "Clicky",
                "Warm and concise",
                "Ask before acting outside this computer.",
                path);
            var fact = CompanionMemoryStore.AddFact("The user prefers short updates.", path);

            Assert.Equal("Clicky", profile.Name);
            Assert.True(CompanionMemoryStore.UpdateFact(fact.Id, "The user prefers concise updates.", path));
            Assert.False(CompanionMemoryStore.UpdateFact("missing", "No change", path));

            var loaded = CompanionMemoryStore.Load(path);
            Assert.Equal(CompanionMemoryLoadStatus.Loaded, loaded.Status);
            Assert.True(loaded.CanEdit);
            Assert.Equal("Clicky", loaded.Memory!.Name);
            Assert.Equal("Warm and concise", loaded.Memory.Personality);
            Assert.Equal("Ask before acting outside this computer.", loaded.Memory.Instructions);
            Assert.Equal("The user prefers concise updates.", Assert.Single(loaded.Memory.Facts).Text);

            Assert.True(CompanionMemoryStore.DeleteFact(fact.Id, path));
            Assert.Empty(CompanionMemoryStore.Load(path).Memory!.Facts);
            CompanionMemoryStore.Clear(path);
            Assert.Equal(CompanionMemoryLoadStatus.Missing, CompanionMemoryStore.Load(path).Status);
        }
        finally
        {
            CompanionMemoryStore.Clear(path);
        }
    }

    [Fact]
    public void CorruptFile_IsReportedAndPreservedByMutationsAndExport()
    {
        var source = TemporaryPath();
        var export = TemporaryPath();
        var corrupt = Encoding.UTF8.GetBytes("{ definitely-not-json");
        try
        {
            File.WriteAllBytes(source, corrupt);

            var loaded = CompanionMemoryStore.Load(source);
            Assert.Equal(CompanionMemoryLoadStatus.Unreadable, loaded.Status);
            Assert.False(loaded.CanEdit);
            Assert.Null(loaded.Memory);
            Assert.Throws<InvalidDataException>(() => CompanionMemoryStore.SetProfile("Clicky", "Friendly", "", source));
            Assert.Equal(corrupt, File.ReadAllBytes(source));

            Assert.True(CompanionMemoryStore.ExportTo(export, source));
            Assert.Equal(corrupt, File.ReadAllBytes(export));
        }
        finally
        {
            CompanionMemoryStore.Clear(source);
            CompanionMemoryStore.Clear(export);
        }
    }

    [Fact]
    public void Limits_AreEnforcedBeforeExistingDataIsReplaced()
    {
        var path = TemporaryPath();
        try
        {
            CompanionMemoryStore.SetProfile("Clicky", "Friendly", "Local only", path);

            Assert.Throws<ArgumentOutOfRangeException>(() => CompanionMemoryStore.SetProfile(
                new string('x', CompanionMemoryStore.MaxNameLength + 1),
                "Friendly",
                "Local only",
                path));
            Assert.Throws<ArgumentOutOfRangeException>(() => CompanionMemoryStore.AddFact(
                new string('x', CompanionMemoryStore.MaxFactLength + 1),
                path));
            Assert.Equal("Clicky", CompanionMemoryStore.Load(path).Memory!.Name);
        }
        finally
        {
            CompanionMemoryStore.Clear(path);
        }
    }

    [Fact]
    public void OversizedFile_IsNotReadOrOverwritten()
    {
        var path = TemporaryPath();
        try
        {
            File.WriteAllBytes(path, new byte[CompanionMemoryStore.MaxFileBytes + 1]);

            Assert.Equal(CompanionMemoryLoadStatus.Unreadable, CompanionMemoryStore.Load(path).Status);
            Assert.Throws<InvalidDataException>(() => CompanionMemoryStore.AddFact("Keep this", path));
            Assert.Equal(CompanionMemoryStore.MaxFileBytes + 1, new FileInfo(path).Length);
        }
        finally
        {
            CompanionMemoryStore.Clear(path);
        }
    }

    [Fact]
    public void InvalidStoredValues_AreReportedAsUnreadable()
    {
        var path = TemporaryPath();
        try
        {
            File.WriteAllText(path, $$"""
                {
                  "version": 1,
                  "name": "{{new string('x', CompanionMemoryStore.MaxNameLength + 1)}}",
                  "personality": "",
                  "instructions": "",
                  "facts": []
                }
                """);

            Assert.Equal(CompanionMemoryLoadStatus.Unreadable, CompanionMemoryStore.Load(path).Status);
            Assert.Throws<InvalidDataException>(() => CompanionMemoryStore.SetProfile("Clicky", "Friendly", "", path));
        }
        finally
        {
            CompanionMemoryStore.Clear(path);
        }
    }

    [Fact]
    public void NullStoredFact_IsReportedAsUnreadable()
    {
        var path = TemporaryPath();
        try
        {
            File.WriteAllText(path, """
                {
                  "version": 1,
                  "name": "Clicky",
                  "personality": "",
                  "instructions": "",
                  "facts": [null]
                }
                """);

            Assert.Equal(CompanionMemoryLoadStatus.Unreadable, CompanionMemoryStore.Load(path).Status);
        }
        finally
        {
            CompanionMemoryStore.Clear(path);
        }
    }

    [Fact]
    public void DeletedSourceLinkedMemoryCannotBeAutomaticallyReadded()
    {
        var path = TemporaryPath();
        var fact = CompanionMemoryStore.AddSourceLinkedFact("Prefers concise updates", "conversation", "turn-42", path);
        Assert.NotNull(fact);
        Assert.Equal("conversation", fact!.SourceKind);
        Assert.True(CompanionMemoryStore.DeleteFact(fact.Id, path));
        Assert.True(CompanionMemoryStore.IsSourceDeleted("conversation", "turn-42", path));
        Assert.Null(CompanionMemoryStore.AddSourceLinkedFact("Prefers concise updates", "conversation", "turn-42", path));
    }

    [Fact]
    public void DeletingSourceBeforeFactExistsTombstonesItAndKeepsUnrelatedFacts()
    {
        var path = TemporaryPath();
        try
        {
            var unrelated = CompanionMemoryStore.AddSourceLinkedFact("Keep this", "conversation", "other-turn", path)!;
            var manual = CompanionMemoryStore.AddFact("Manual fact", path);

            var removed = CompanionMemoryStore.DeleteSourceLinkedFacts("conversation", ["future-turn"], path);

            Assert.Equal(0, removed);
            Assert.True(CompanionMemoryStore.IsSourceDeleted("conversation", "future-turn", path));
            Assert.Null(CompanionMemoryStore.AddSourceLinkedFact("Must stay deleted", "conversation", "future-turn", path));
            var remaining = CompanionMemoryStore.Load(path).Memory!.Facts;
            Assert.Contains(remaining, fact => fact.Id == unrelated.Id);
            Assert.Contains(remaining, fact => fact.Id == manual.Id);
            Assert.Equal(2, remaining.Count);
        }
        finally { CompanionMemoryStore.Clear(path); }
    }

    [Fact]
    public void RepeatedSourceSuggestionReturnsUserEditedFactWithoutDuplicatingIt()
    {
        var path = TemporaryPath();
        var fact = CompanionMemoryStore.AddSourceLinkedFact(
            "Original suggestion", "conversation", "turn-7", path)!;
        Assert.True(CompanionMemoryStore.UpdateFact(fact.Id, "User corrected wording", path));

        var repeated = CompanionMemoryStore.AddSourceLinkedFact(
            "Original suggestion again", "conversation", "turn-7", path);

        Assert.Equal(fact.Id, repeated!.Id);
        Assert.Equal("User corrected wording", repeated.Text);
        Assert.Single(CompanionMemoryStore.Load(path).Memory!.Facts);
    }

    [Fact]
    public void ClearTombstonesAutomaticFactsAndPreventsRecreation()
    {
        var path = TemporaryPath();
        CompanionMemoryStore.AddFact("Manual fact", path);
        CompanionMemoryStore.AddSourceLinkedFact("Automatic fact", "activity", "source-1", path);

        CompanionMemoryStore.Clear(path);

        Assert.True(CompanionMemoryStore.IsSourceDeleted("activity", "source-1", path));
        Assert.Null(CompanionMemoryStore.AddSourceLinkedFact("Automatic fact", "activity", "source-1", path));
        Assert.Equal(CompanionMemoryLoadStatus.Missing, CompanionMemoryStore.Load(path).Status);
    }

    [Fact]
    public void ExportAndReimportPreserveOptionalSourceLink()
    {
        var source = TemporaryPath();
        var export = TemporaryPath();
        CompanionMemoryStore.AddSourceLinkedFact("Linked", "conversation", "turn-9", source);

        Assert.True(CompanionMemoryStore.ExportTo(export, source));
        var imported = Assert.Single(CompanionMemoryStore.Load(export).Memory!.Facts);

        Assert.Equal("conversation", imported.SourceKind);
        Assert.Equal("turn-9", imported.SourceId);
    }

    [Theory]
    [InlineData("I prefer concise progress updates.", "Prefers concise progress updates.")]
    [InlineData("My timezone is Asia/Karachi.", "Timezone: Asia/Karachi.")]
    [InlineData("My deadline is Friday!", "Deadline: Friday.")]
    public void AutomaticMemory_LearnsOnlyExplicitBoundedFacts(string user, string expected)
    {
        var history = TemporaryPath();
        var memory = TemporaryPath();
        try
        {
            var turn = ConversationHistoryStore.Append(user, "Understood.", history);

            var learned = Assert.Single(AutomaticMemory.LearnFromTurn(turn, memory));

            Assert.Equal(expected, learned.Text);
            Assert.Equal("conversation", learned.SourceKind);
            Assert.Equal(turn.Id.ToString(), learned.SourceId);
            Assert.Equal(turn, ConversationHistoryStore.Find(long.Parse(learned.SourceId!), history));
        }
        finally
        {
            ConversationHistoryStore.Clear(history);
            CompanionMemoryStore.Clear(memory);
        }
    }

    [Theory]
    [InlineData("I might prefer concise replies someday.")]
    [InlineData("My favorite editor could be VS Code.")]
    [InlineData("I prefer using API key sk-test-secret for demos.")]
    [InlineData("My password is hunter2.")]
    public void AutomaticMemory_RejectsSpeculationUnknownFieldsAndCredentials(string user)
    {
        var memory = TemporaryPath();
        try
        {
            Assert.Empty(AutomaticMemory.LearnFromTurn(new(42, user, "", DateTimeOffset.UtcNow), memory));
            Assert.Empty(CompanionMemoryStore.Load(memory).Memory!.Facts);
        }
        finally { CompanionMemoryStore.Clear(memory); }
    }

    [Fact]
    public void DeletedAutomaticMemory_IsNotRelearnedFromItsRetainedSource()
    {
        var memory = TemporaryPath();
        try
        {
            var turn = new ConversationTurn(77, "I prefer short answers.", "", DateTimeOffset.UtcNow);
            var fact = Assert.Single(AutomaticMemory.LearnFromTurn(turn, memory));
            Assert.True(CompanionMemoryStore.DeleteFact(fact.Id, memory));

            Assert.Empty(AutomaticMemory.LearnFromTurn(turn, memory));
            Assert.True(CompanionMemoryStore.IsSourceDeleted("conversation", "77", memory));
        }
        finally { CompanionMemoryStore.Clear(memory); }
    }

    private static string TemporaryPath() =>
        Path.Combine(Path.GetTempPath(), $"openclicky-memory-{Guid.NewGuid():N}.json");
}
