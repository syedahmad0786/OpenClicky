using OpenClicky.Core.Text;

namespace OpenClicky.Core.Tests;

public class LevenshteinTests
{
    [Fact] public void KittenSitting_IsThree() => Assert.Equal(3, Levenshtein.Distance("kitten", "sitting"));
    [Fact] public void EmptyVsWord_IsItsLength() { Assert.Equal(3, Levenshtein.Distance("", "abc")); Assert.Equal(3, Levenshtein.Distance("abc", "")); }
    [Fact] public void EqualStrings_AreZero() => Assert.Equal(0, Levenshtein.Distance("same", "same"));
    [Fact] public void IsCaseSensitive() => Assert.Equal(1, Levenshtein.Distance("a", "A"));
}
