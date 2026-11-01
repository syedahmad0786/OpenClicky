using OpenClicky.Core;

namespace OpenClicky.Core.Tests;

public class DictationInsertionProofTests
{
    [Theory]
    [InlineData("Hello ", "Hello world", "world")]
    [InlineData("Hello old world", "Hello new world", "new")]
    [InlineData("aaaa", "aaaaa", "a")]
    [InlineData("same edges", "same new edges", "new ")]
    public void ExactSingleInsertionOrReplacement_IsVerified(string before, string after, string inserted) =>
        Assert.True(DictationInsertionProof.IsExactContiguousChange(before, after, inserted));

    [Theory]
    [InlineData("target old", "target new", "target")]
    [InlineData("unchanged", "unchanged", "unchanged")]
    [InlineData("Hello ", "Hello world!", "world")]
    [InlineData("one two", "one added and two", "added")]
    [InlineData("", "first shelf item", "first shelf item\r\n")]
    [InlineData(null, "text", "text")]
    [InlineData("", "text", "")]
    public void AmbiguousOrDifferentChange_IsUnverified(string? before, string after, string inserted) =>
        Assert.False(DictationInsertionProof.IsExactContiguousChange(before, after, inserted));
}
