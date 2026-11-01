using OpenClicky.Core.Dictation;

namespace OpenClicky.Core.Tests;

public class DictationFieldContextTests
{
    [Fact]
    public void CaretInTheMiddleSplitsBeforeAndAfter()
    {
        var c = DictationFieldContext.FromValue("Notepad", null, "Hello world", 5, 0, singleLine: true);
        Assert.Equal("Hello", c.BeforeText);
        Assert.Equal(" world", c.AfterText);
        Assert.Equal("", c.SelectedText);
        Assert.Equal("Notepad", c.AppName);
        Assert.True(c.SingleLine);
    }

    [Fact]
    public void ASelectionFillsSelectedTextAndLeavesBothSides()
    {
        var c = DictationFieldContext.FromValue("App", "example.org", "Hello big world", 6, 3, singleLine: false);
        Assert.Equal("Hello ", c.BeforeText);
        Assert.Equal("big", c.SelectedText);
        Assert.Equal(" world", c.AfterText);
        Assert.Equal("example.org", c.SiteHost);
        Assert.False(c.SingleLine);
    }

    [Fact]
    public void LongTextIsCappedWithBeforeTakenFromTheEnd()
    {
        var value = new string('a', 500) + new string('b', 2000) + new string('c', 2000) + new string('d', 500);
        var c = DictationFieldContext.FromValue("App", null, value, 2500, 0, false);
        Assert.Equal(new string('b', 2000), c.BeforeText);
        Assert.Equal(new string('c', 2000), c.AfterText);
    }

    [Fact]
    public void SelectedTextIsCapped()
    {
        var c = DictationFieldContext.FromValue("App", null, new string('x', 5000), 0, 5000, false);
        Assert.Equal(2000, c.SelectedText.Length);
        Assert.Equal("", c.BeforeText);
        Assert.Equal("", c.AfterText);
    }

    [Theory]
    [InlineData(-4, 0, "", "Hello")]
    [InlineData(99, 0, "Hello", "")]
    [InlineData(3, 99, "Hel", "")]
    public void OutOfRangeIndicesAreClamped(int start, int length, string before, string after)
    {
        var c = DictationFieldContext.FromValue("App", null, "Hello", start, length, true);
        Assert.Equal(before, c.BeforeText);
        Assert.Equal(after, c.AfterText);
    }

    [Theory]
    [InlineData("https://Mail.Google.com/mail/u/0", "mail.google.com")]
    [InlineData("example.org/path", "example.org")]
    [InlineData("  http://localhost:3000/x ", "localhost")]
    [InlineData("hello world", null)]
    [InlineData("chrome://settings", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void HostOfReadsTheHost(string? text, string? host) =>
        Assert.Equal(host, DictationFieldContext.HostOf(text));
}
