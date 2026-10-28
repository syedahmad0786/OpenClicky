using OpenClicky.Core.Work;

namespace OpenClicky.Core.Tests;

public class FileCarryOverTests
{
    private static void InTemp(Action<string> test)
    {
        var dir = Path.Combine(Path.GetTempPath(), "openclicky-carry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try { test(dir); } finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void MissingOrBlankPreviousCopiesNothing() => InTemp(dir =>
    {
        var next = Path.Combine(dir, "next.txt");
        Assert.False(FileCarryOver.CopyIfMissing(null, next));
        Assert.False(FileCarryOver.CopyIfMissing(" ", next));
        Assert.False(FileCarryOver.CopyIfMissing(Path.Combine(dir, "gone.txt"), next));
        Assert.False(File.Exists(next));
    });

    [Fact]
    public void CopiesOnceAndLeavesTheOldFileUnchanged() => InTemp(dir =>
    {
        var old = Path.Combine(dir, "old.txt");
        var next = Path.Combine(dir, "sub", "next.txt");
        File.WriteAllText(old, "mine");
        Assert.True(FileCarryOver.CopyIfMissing(old, next));
        Assert.Equal("mine", File.ReadAllText(next));
        Assert.Equal("mine", File.ReadAllText(old));
        File.WriteAllText(old, "changed");
        Assert.False(FileCarryOver.CopyIfMissing(old, next));
        Assert.Equal("mine", File.ReadAllText(next));
    });

    [Fact]
    public void ExistingNextIsUntouched() => InTemp(dir =>
    {
        var old = Path.Combine(dir, "old.txt");
        var next = Path.Combine(dir, "next.txt");
        File.WriteAllText(old, "old");
        File.WriteAllText(next, "new");
        Assert.False(FileCarryOver.CopyIfMissing(old, next));
        Assert.Equal("new", File.ReadAllText(next));
        Assert.Equal("old", File.ReadAllText(old));
        Assert.False(FileCarryOver.CopyIfMissing(old, old));
    });

    [Fact]
    public void UnwritableTargetReturnsFalse() => InTemp(dir =>
    {
        var old = Path.Combine(dir, "old.txt");
        File.WriteAllText(old, "x");
        var blocker = Path.Combine(dir, "a-file");
        File.WriteAllText(blocker, "");
        Assert.False(FileCarryOver.CopyIfMissing(old, Path.Combine(blocker, "next.txt")));
    });
}
