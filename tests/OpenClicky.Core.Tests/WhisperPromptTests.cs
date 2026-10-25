using OpenClicky.Core.Dictation;

namespace OpenClicky.Core.Tests;

public class WhisperPromptTests
{
    [Fact]
    public void EmptyDictionaryHasNoPrompt() => Assert.Null(WhisperPrompt.FromDictionary([]));

    [Fact]
    public void WordsAreJoinedWithCommas() => Assert.Equal("OpenClicky, Ahmad, Whisper", WhisperPrompt.FromDictionary(["OpenClicky", "Ahmad", "Whisper"]));

    [Fact]
    public void LongListIsCutAtAWordBoundaryWithin600Characters()
    {
        var words = Enumerable.Range(0, 71).Select(i => "word" + i.ToString("D4")).ToList(); // 8 chars + ", " each: 708
        Assert.True(string.Join(", ", words).Length >= 700);
        var prompt = WhisperPrompt.FromDictionary(words)!;
        Assert.True(prompt.Length <= 600, prompt.Length.ToString());
        var kept = prompt.Split(", ");
        Assert.Equal(words.Take(kept.Length), kept); // whole words only, in order
        Assert.True(prompt.Length + 2 + words[kept.Length].Length > 600); // the next word would not fit
    }

    [Fact]
    public void WordsEndingExactlyAt600AreAllKept()
    {
        var words = Enumerable.Range(0, 59).Select(i => "word" + i.ToString("D4")).Append("tenletters").ToList(); // exactly 600
        Assert.Equal(600, WhisperPrompt.FromDictionary([.. words, "next"])!.Length);
    }
}
