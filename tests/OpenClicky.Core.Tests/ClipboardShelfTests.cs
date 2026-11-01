using OpenClicky.Core;

namespace OpenClicky.Core.Tests;

public class ClipboardShelfTests
{
    [Fact]
    public void ItemsPreserveExactTextContextAndOrder()
    {
        var shelf = new ClipboardShelf(TemporaryPath());
        var when = new DateTimeOffset(2026, 10, 12, 1, 2, 3, TimeSpan.Zero);

        Assert.Equal(ClipboardShelfAddResult.Added, shelf.TryAdd("00123\r\nline two", "Calculator", when));
        Assert.Equal(ClipboardShelfAddResult.Added, shelf.TryAdd(" https://example.test/a?x=1 ", null, when.AddSeconds(1)));

        Assert.Equal("00123\r\nline two", shelf.Items[0].Text);
        Assert.Equal("Calculator", shelf.Items[0].Source);
        Assert.Equal(when, shelf.Items[0].AddedAtUtc);
        Assert.Equal("00123\r\nline two| https://example.test/a?x=1 ", shelf.Combine("|"));
    }

    [Fact]
    public void ImmediateDuplicatesAndBoundsAreEnforced()
    {
        var shelf = new ClipboardShelf(TemporaryPath());

        Assert.Equal(ClipboardShelfAddResult.Added, shelf.TryAdd("same", null));
        Assert.Equal(ClipboardShelfAddResult.DuplicateIgnored, shelf.TryAdd("same", "Another app"));
        Assert.Equal(ClipboardShelfAddResult.TooLarge, shelf.TryAdd(new string('x', ClipboardShelf.MaxItemLength + 1), null));

        for (var index = 1; index < ClipboardShelf.MaxItems; index++)
        {
            Assert.Equal(ClipboardShelfAddResult.Added, shelf.TryAdd(index.ToString(), null));
        }
        Assert.Equal(ClipboardShelfAddResult.Full, shelf.TryAdd("one too many", null));
        Assert.Equal(ClipboardShelf.MaxItems, shelf.Items.Count);
    }

    [Fact]
    public void TakeNextCyclesAndRemovalKeepsCursorValid()
    {
        var shelf = new ClipboardShelf(TemporaryPath());
        shelf.TryAdd("first", null);
        shelf.TryAdd("second", null);
        shelf.TryAdd("third", null);

        Assert.Equal("first", shelf.TakeNext()!.Text);
        Assert.Equal("second", shelf.TakeNext()!.Text);
        Assert.True(shelf.Remove(shelf.Items[0].Id));
        Assert.Equal("third", shelf.TakeNext()!.Text);
        Assert.Equal("second", shelf.TakeNext()!.Text);
        Assert.False(shelf.Remove(long.MaxValue));
    }

    [Fact]
    public void PeekAndConditionalAdvancePreserveSequenceUntilVerifiedWrite()
    {
        var path = TemporaryPath();
        var shelf = new ClipboardShelf(path);
        shelf.TryAdd("first", null);
        shelf.TryAdd("second", null);

        var first = shelf.PeekNext()!;
        Assert.Same(first, shelf.PeekNext());
        Assert.False(shelf.AdvanceNext(long.MaxValue));
        Assert.Equal("first", new ClipboardShelf(path).PeekNext()!.Text);

        Assert.True(shelf.AdvanceNext(first.Id));
        Assert.False(shelf.AdvanceNext(first.Id));
        Assert.Equal("second", shelf.PeekNext()!.Text);
        Assert.Equal("second", new ClipboardShelf(path).PeekNext()!.Text);
    }

    [Fact]
    public void ExactTextEditsPinsCollectionsAndCollectionStatePersist()
    {
        var path = TemporaryPath();
        var shelf = new ClipboardShelf(path);
        shelf.SetCollecting(true);
        Assert.Equal(ClipboardShelfAddResult.Added, shelf.TryAdd(" 001\r\nnext ", "Source", collection: "Research"));
        var id = Assert.Single(shelf.Items).Id;
        Assert.True(shelf.Edit(id, "\tchanged exactly\r\n"));
        Assert.True(shelf.SetPinned(id, true));
        Assert.True(shelf.SetCollection(id, "Inbox"));

        var restored = new ClipboardShelf(path);
        var item = Assert.Single(restored.Items);
        Assert.Equal("\tchanged exactly\r\n", item.Text);
        Assert.True(item.IsPinned);
        Assert.Equal("Inbox", item.Collection);
        Assert.True(restored.IsCollecting);
    }

    [Fact]
    public void CursorAndReorderedItemsPersist()
    {
        var path = TemporaryPath();
        var shelf = new ClipboardShelf(path);
        shelf.TryAdd("first", null);
        shelf.TryAdd("second", null);
        shelf.TryAdd("third", null);
        Assert.Equal("first", shelf.TakeNext()!.Text);
        Assert.True(shelf.Move(shelf.Items[2].Id, 0));

        var restored = new ClipboardShelf(path);
        Assert.Equal(new[] { "third", "first", "second" }, restored.Items.Select(item => item.Text));
        Assert.Equal("third", restored.TakeNext()!.Text);
        Assert.Equal("first", new ClipboardShelf(path).TakeNext()!.Text);
    }

    [Fact]
    public void FailedDatabaseWriteDoesNotChangeInMemoryItemOrCollectionState()
    {
        var path = TemporaryPath();
        var shelf = new ClipboardShelf(path);
        shelf.TryAdd("original", null);
        var id = Assert.Single(shelf.Items).Id;
        File.WriteAllText(Path.ChangeExtension(path, ".db"), "not a database");

        Assert.ThrowsAny<Exception>(() => shelf.Edit(id, "changed"));
        Assert.Equal("original", Assert.Single(shelf.Items).Text);
        Assert.ThrowsAny<Exception>(() => shelf.SetCollecting(true));
        Assert.False(shelf.IsCollecting);
    }

    [Fact]
    public void FailedCursorWriteLeavesCurrentItemPending()
    {
        var path = TemporaryPath();
        var shelf = new ClipboardShelf(path);
        shelf.TryAdd("first", null);
        shelf.TryAdd("second", null);
        var first = shelf.PeekNext()!;
        File.WriteAllText(Path.ChangeExtension(path, ".db"), "not a database");

        Assert.ThrowsAny<Exception>(() => shelf.AdvanceNext(first.Id));
        Assert.Equal(first.Id, shelf.PeekNext()!.Id);
    }

    private static string TemporaryPath() => Path.Combine(Path.GetTempPath(), $"openclicky-shelf-{Guid.NewGuid():N}.json");
}
