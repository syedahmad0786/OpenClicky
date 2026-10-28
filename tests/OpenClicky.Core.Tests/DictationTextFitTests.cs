using OpenClicky.Core.Dictation;

namespace OpenClicky.Core.Tests;

public class DictationTextFitTests
{
    [Fact]
    public void AfterAWordAddsALeadingSpace() =>
        Assert.Equal(" world", DictationTextFit.Fit("world", "Hello", "", false));

    [Theory]
    [InlineData("Hello ")]
    [InlineData("(")]
    [InlineData("")]
    [InlineData("Line\n")]
    public void AfterSpaceBracketOrNothingAddsNoLeadingSpace(string before) =>
        Assert.Equal("world", DictationTextFit.Fit("world", before, "", false));

    [Theory]
    [InlineData(",")]
    [InlineData(".")]
    [InlineData(";")]
    [InlineData(":")]
    [InlineData("!")]
    [InlineData("?")]
    [InlineData(")")]
    public void PunctuationFirstAddsNoLeadingSpace(string first) =>
        Assert.Equal(first + " then", DictationTextFit.Fit(first + " then", "Hello", "", false));

    [Theory]
    [InlineData("")]
    [InlineData("Done.")]
    [InlineData("Done. ")]
    [InlineData("Really!")]
    [InlineData("Why?")]
    [InlineData("Line\n")]
    public void LeadingCommaIsDroppedAtASentenceStart(string before) =>
        Assert.Equal("and then", DictationTextFit.Fit(", and then", before, "", false).TrimStart());

    [Fact]
    public void LeadingCommaIsKeptMidSentence() =>
        Assert.Equal(", and then", DictationTextFit.Fit(", and then", "Hello", "", false));

    [Theory]
    [InlineData("world", "Hello ")]
    [InlineData("42", "Hello ")]
    [InlineData(" world", "Hello")]
    [InlineData(".", "Hello")]
    [InlineData("", "Hello")]
    public void AWordAfterTheCaretAddsATrailingSpace(string after, string expected) =>
        Assert.Equal(expected, DictationTextFit.Fit("Hello", "", after, false));

    [Theory]
    [InlineData("你好", "世界")]
    [InlineData("こんにちは", "カタカナ")]
    [InlineData("สวัสดี", "ครับ")]
    public void NoSpaceScriptsJoinWithoutSpaces(string around, string text) =>
        Assert.Equal(text, DictationTextFit.Fit(text, around, around, false));

    [Fact]
    public void MixedScriptSeamStillGetsASpace() =>
        Assert.Equal(" 你好 ", DictationTextFit.Fit("你好", "Hello", "world", false));

    [Fact]
    public void SingleLineTurnsNewlinesIntoSpaces()
    {
        Assert.Equal("one two three", DictationTextFit.Fit("one\ntwo \r\n three", "", "", singleLine: true));
        Assert.Equal("one\ntwo", DictationTextFit.Fit("one\ntwo", "", "", singleLine: false));
    }

    [Fact]
    public void DoubleHyphenBecomesAnEmDash() =>
        Assert.Equal("wait — no", DictationTextFit.Fit("wait -- no", "", "", false));
}
