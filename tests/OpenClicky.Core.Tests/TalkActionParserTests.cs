using OpenClicky.Core;

namespace OpenClicky.Core.Tests;

public class TalkActionParserTests
{
    [Fact]
    public void ParsesEachTagAndStripsThemInOrder()
    {
        var r = TalkActionParser.Parse("opening it. [OPEN:https://example.com/a?b=1] copied. [COPY:hello world] typing. [TYPE:  hi there ]");
        Assert.Equal("opening it. copied. typing.", r.Text);
        Assert.Equal(
            [new TalkAction(TalkActionKind.Open, "https://example.com/a?b=1"),
             new TalkAction(TalkActionKind.Copy, "hello world"),
             new TalkAction(TalkActionKind.Type, "hi there")], r.Actions);
    }

    [Fact]
    public void KeepsOnlyTheFirstThreeButStripsAll()
    {
        var r = TalkActionParser.Parse("a [COPY:1] b [COPY:2] c [COPY:3] d [COPY:4] e");
        Assert.Equal(["1", "2", "3"], r.Actions.Select(a => a.Value));
        Assert.Equal("a b c d e", r.Text);
    }

    [Fact]
    public void EscapedBracketStaysInsideCopyAndTypeText()
    {
        var r = TalkActionParser.Parse(@"done [TYPE:list[0\] = 1] ok");
        Assert.Equal("done ok", r.Text);
        Assert.Equal(new TalkAction(TalkActionKind.Type, "list[0] = 1"), Assert.Single(r.Actions));
    }

    [Fact]
    public void TextOverTwoThousandCharsIsDroppedButStillStripped()
    {
        var ok = new string('x', 2000);
        var r = TalkActionParser.Parse($"a [COPY:{ok}] b [TYPE:{ok}y] c");
        Assert.Equal("a b c", r.Text);
        Assert.Equal(new TalkAction(TalkActionKind.Copy, ok), Assert.Single(r.Actions));
    }

    [Theory]
    [InlineData("file:///c:/windows/system32/calc.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://example.com/x")]
    [InlineData("https://user:pw@example.com/")]
    [InlineData("https://user@example.com/")]
    [InlineData("example.com/relative")]
    [InlineData("")]
    public void OpenWithoutAPlainHttpUrlIsDroppedButStripped(string url)
    {
        var r = TalkActionParser.Parse($"go [OPEN:{url}] now");
        Assert.Empty(r.Actions);
        Assert.Equal("go now", r.Text);
    }

    [Fact]
    public void EmptyCopyIsDropped()
    {
        var r = TalkActionParser.Parse("x [COPY:   ] y");
        Assert.Empty(r.Actions);
        Assert.Equal("x y", r.Text);
    }

    [Fact]
    public void TagsInsideCodeFencesAreNotParsedOrStripped()
    {
        var reply = "here [COPY:real]\n```ag-ui\n{\"a\":\"[OPEN:https://x.com] [TYPE:no]\"}\n```";
        var r = TalkActionParser.Parse(reply);
        Assert.Equal(new TalkAction(TalkActionKind.Copy, "real"), Assert.Single(r.Actions));
        Assert.Equal("here\n```ag-ui\n{\"a\":\"[OPEN:https://x.com] [TYPE:no]\"}\n```", r.Text);
    }

    [Theory]
    [InlineData("see [OPEN:https://example.com never closed")]
    [InlineData("see [COPY:abc")]
    [InlineData("see [OPENX:https://example.com] and [open:https://example.com] and [POINT:1,2:a]")]
    [InlineData("plain text, no tags")]
    public void MalformedOrUnclosedTagsAreLeftAsText(string reply)
    {
        var r = TalkActionParser.Parse(reply);
        Assert.Empty(r.Actions);
        Assert.Equal(reply, r.Text);
    }

    [Fact]
    public void PartialModeCutsAnUnclosedTagSoItIsNeverSpoken()
    {
        Assert.Equal("see", TalkActionParser.Parse(@"see [COPY:abc \] still open", partial: true).Text);
        Assert.Equal("see [COPY:abc", TalkActionParser.Parse("see [COPY:abc").Text);
    }

    [Fact]
    public void DoubleBackslashIsALiteralBackslash()
    {
        var r = TalkActionParser.Parse(@"path [TYPE:C:\\Users\\me] then [COPY:ends with \\] and [COPY:a\b \] c]");
        Assert.Equal("path then and", r.Text);
        Assert.Equal([@"C:\Users\me", @"ends with \", @"a\b ] c"], r.Actions.Select(a => a.Value));
    }

    [Fact]
    public void OpenStoresTheNormalisedAddressAndCapsItsLength()
    {
        Assert.Equal("https://example.com/a%20b", Assert.Single(TalkActionParser.Parse("[OPEN:HTTPS://Example.COM/a b]").Actions).Value);
        Assert.Empty(TalkActionParser.Parse($"[OPEN:https://example.com/{new string('a', 2000)}]").Actions);
    }

    [Fact]
    public void MarkLabelsThatLookLikeActionsAreLeftAlone()
    {
        const string reply = "here [MARK:text:10,20:OPEN:https://example.com:screen1] and [MARK:rect:1,2,3,4:COPY: TYPE:x:screen1]";
        var r = TalkActionParser.Parse(reply);
        Assert.Empty(r.Actions);
        Assert.Equal(reply, r.Text);
    }

    [Fact]
    public void ManyUnclosedTagsScanInLinearTime()
    {
        var reply = "a [COPY:x] b " + string.Concat(Enumerable.Repeat("[COPY:", 50_000));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var r = TalkActionParser.Parse(reply);
        Assert.True(clock.ElapsedMilliseconds < 1_000, $"took {clock.ElapsedMilliseconds} ms");
        Assert.Equal("a b " + reply[13..], r.Text);
        Assert.Equal("x", Assert.Single(r.Actions).Value);
    }

    [Theory]
    [InlineData("opening [OPEN:http", "opening")]
    [InlineData("typing [TYPE:", "typing")]
    [InlineData(@"typing [TYPE:C:\\Us", "typing")]
    public void PartialModeCutsSplitOpenAndTypeTags(string partial, string expected) =>
        Assert.Equal(expected, TalkActionParser.Parse(partial, partial: true).Text);

    [Fact]
    public void CoexistsWithTrailingPoint()
    {
        var r = TalkActionParser.Parse("opening it [OPEN:https://example.com] [POINT:10,20:button]");
        Assert.Single(r.Actions);
        var point = PointParser.Parse(r.Text);
        Assert.Equal("opening it", point.SpokenText);
        Assert.Equal(10, point.X);
    }
}
