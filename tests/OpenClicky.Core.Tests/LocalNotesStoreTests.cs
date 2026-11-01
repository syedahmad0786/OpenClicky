using System.Text;
using OpenClicky.Core;

namespace OpenClicky.Core.Tests;

public class LocalNotesStoreTests
{
    [Fact]
    public void Notes_CanBeAddedUpdatedDeletedSearchedAndRead()
    {
        var path = TemporaryPath();
        try
        {
            var first = LocalNotesStore.SaveNote(null, "Inbox follow-up", "Reply to Sam", path);
            var second = LocalNotesStore.Add("Weekend", "Buy coffee", path);

            Assert.Equal(2, LocalNotesStore.GetRecords(path).Count);
            Assert.Equal(first, Assert.Single(LocalNotesStore.Search("sam", path)));
            Assert.Equal(second, Assert.Single(LocalNotesStore.Search("week", path)));
            Assert.True(LocalNotesStore.Update(first.Id, "Inbox", "Reply Monday", path));
            Assert.False(LocalNotesStore.Update("missing", "No", "Change", path));
            Assert.Equal("Reply Monday", LocalNotesStore.GetRecords(path).Single(note => note.Id == first.Id).Text);
            Assert.True(LocalNotesStore.Delete(second.Id, path));
            Assert.False(LocalNotesStore.Delete(second.Id, path));
            Assert.Single(LocalNotesStore.GetRecords(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CorruptFile_IsReportedAndNeverOverwritten()
    {
        var path = TemporaryPath();
        var corrupt = Encoding.UTF8.GetBytes("{ not-json");
        try
        {
            File.WriteAllBytes(path, corrupt);

            var result = LocalNotesStore.Load(path);
            Assert.Equal(LocalNotesLoadStatus.Unreadable, result.Status);
            Assert.False(result.CanEdit);
            Assert.Null(result.Notes);
            Assert.Throws<InvalidDataException>(() => LocalNotesStore.Add("Keep", "This", path));
            Assert.Throws<InvalidDataException>(() => LocalNotesStore.GetRecords(path));
            Assert.Equal(corrupt, File.ReadAllBytes(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Limits_AreEnforcedBeforeExistingNotesAreReplaced()
    {
        var path = TemporaryPath();
        try
        {
            var note = LocalNotesStore.Add("Original", "Keep me", path);
            Assert.Throws<ArgumentOutOfRangeException>(() => LocalNotesStore.Update(
                note.Id,
                new string('t', LocalNotesStore.MaxTitleLength + 1),
                "Changed",
                path));
            Assert.Throws<ArgumentOutOfRangeException>(() => LocalNotesStore.Add(
                "Too long",
                new string('x', LocalNotesStore.MaxTextLength + 1),
                path));
            Assert.Equal("Keep me", Assert.Single(LocalNotesStore.GetRecords(path)).Text);

            for (var index = 1; index < LocalNotesStore.MaxNotes; index++)
            {
                LocalNotesStore.Add($"Note {index}", string.Empty, path);
            }
            Assert.Throws<InvalidOperationException>(() => LocalNotesStore.Add("One too many", "", path));
            Assert.Equal(LocalNotesStore.MaxNotes, LocalNotesStore.GetRecords(path).Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MarkdownExport_IsReadableAndLeavesNoTemporaryFile()
    {
        var path = TemporaryPath();
        var export = TemporaryPath(".md");
        try
        {
            LocalNotesStore.Add("Inbox\nitem", "**Call** tomorrow", path);
            LocalNotesStore.ExportMarkdown(export, path);

            var markdown = File.ReadAllText(export);
            Assert.Contains("# OpenClicky Notes", markdown);
            Assert.Contains("## Inbox item", markdown);
            Assert.Contains("**Call** tomorrow", markdown);
            Assert.Throws<ArgumentException>(() => LocalNotesStore.ExportMarkdown(path, path));
            Assert.Equal("**Call** tomorrow", Assert.Single(LocalNotesStore.GetRecords(path)).Text);
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(export)!, $".{Path.GetFileName(export)}.*.tmp"));
        }
        finally
        {
            File.Delete(path);
            File.Delete(export);
        }
    }

    [Fact]
    public void LegacyJsonImportsTransactionallyAndRemainsUntouched()
    {
        var path = TemporaryPath();
        var original = """
            {"version":1,"notes":[{"id":"legacy","title":"Demo fact","text":"Keep this","updatedAtUtc":"2026-10-12T00:00:00+00:00"}]}
            """;
        File.WriteAllText(path, original);

        var note = Assert.Single(LocalNotesStore.GetRecords(path));

        Assert.Equal("Demo fact", note.Title);
        Assert.Equal(original, File.ReadAllText(path));
        Assert.True(File.Exists(Path.ChangeExtension(path, ".db")));
    }

    [Fact]
    public void NotesAreAvailableThroughLocalFullTextSearch()
    {
        var path = TemporaryPath();
        var note = LocalNotesStore.Add("Research", "a uniquely searchable needle", path);

        var result = Assert.Single(LocalSearchStore.Search("searchable needle", path));
        Assert.Equal("note", result.Kind);
        Assert.Equal(note.Id, result.RecordId);
    }

    private static string TemporaryPath(string extension = ".json") =>
        Path.Combine(Path.GetTempPath(), $"openclicky-notes-{Guid.NewGuid():N}{extension}");
}
