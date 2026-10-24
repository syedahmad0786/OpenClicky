using OpenClicky.Core;
using OpenClicky.Core.Shell;

namespace OpenClicky.Core.Tests;

public class TalkActionTextTests
{
    [Fact]
    public void TypingTextHasNoControlCharacters() =>
        Assert.Equal("a  b c d e", TalkActionText.SanitizeForTyping("a\r\nb\tc\u0007d\u0085e"));

    [Fact]
    public void UndoOnlyWhileTheClipboardStillHoldsOurText()
    {
        Assert.True(TalkActionText.CanUndoCopy("copied", "copied"));
        Assert.False(TalkActionText.CanUndoCopy("something newer", "copied"));
        Assert.False(TalkActionText.CanUndoCopy("Copied", "copied"));
        Assert.False(TalkActionText.CanUndoCopy(null, "copied"));
    }

    [Fact]
    public void CopiedCardShowsTheFirstSixtyCharactersAndOffersUndo()
    {
        var card = TalkActionText.Copied("line one\n" + new string('x', 100));
        Assert.Equal(TalkActionText.CopyCardId, card.Id);
        Assert.Equal(NotchCardKind.Clipboard, card.Kind);
        Assert.Equal("Copied: line one " + new string('x', 51) + "…", card.Title);
        Assert.Equal(TalkActionText.UndoAction, Assert.Single(card.Actions).Id);
        Assert.Equal("Copied: short", TalkActionText.Copied("short").Title);
    }

    [Fact]
    public void PreviewAndOpenedCardsShowTheFullValue()
    {
        var text = new string('t', 2000);
        Assert.Equal(text, TalkActionText.Preview(new(TalkActionKind.Copy, text)).Body);
        Assert.Equal("a b", TalkActionText.Preview(new(TalkActionKind.Type, "a\nb")).Body);
        var url = "https://example.com/" + new string('p', 1900);
        var preview = TalkActionText.Preview(new(TalkActionKind.Open, url));
        Assert.Equal(url, preview.Body);
        Assert.Empty(preview.Actions);
        Assert.Equal(url, TalkActionText.Opened(url).Body);
        Assert.NotEqual(TalkActionText.Preview(new(TalkActionKind.Copy, "a")).Id, TalkActionText.Preview(new(TalkActionKind.Copy, "a")).Id);
    }

    [Fact]
    public void SnippetNeverSplitsASurrogatePair()
    {
        var text = new string('a', 59) + "\U0001F600" + "tail";
        Assert.Equal(new string('a', 59) + "…", TalkActionText.Snippet(text));
        Assert.Equal(new string('a', 58) + "\U0001F600…", TalkActionText.Snippet(new string('a', 58) + "\U0001F600" + "tail"));
    }

    [Fact]
    public void LookalikeHostsAlsoShowTheirPunycodeHost()
    {
        const string cyrillic = "https://аpple.com/";
        Assert.Contains("xn--pple-43d.com", TalkActionText.DescribeUrl(cyrillic));
        Assert.StartsWith(cyrillic, TalkActionText.DescribeUrl(cyrillic));
        Assert.Contains("xn--pple-43d.com", TalkActionText.ConfirmSummary(new(TalkActionKind.Open, cyrillic)));
        Assert.Contains("xn--pple-43d.com", TalkActionText.Opened(cyrillic).Body);
        Assert.Contains("xn--pple-43d.com", TalkActionText.Preview(new(TalkActionKind.Open, cyrillic)).Body);
        Assert.Equal("https://example.com/", TalkActionText.DescribeUrl("https://example.com/"));
        Assert.Equal("http://[::1]:8080/", TalkActionText.DescribeUrl("http://[::1]:8080/"));
        Assert.Equal("http://[2001:db8::1]/a", TalkActionText.DescribeUrl("http://[2001:db8::1]/a"));
        Assert.Equal("http://192.168.0.1/", TalkActionText.DescribeUrl("http://192.168.0.1/"));
    }

    [Fact]
    public void TheLongestAllowedLookalikeAddressFitsACardBody()
    {
        var host = string.Join('.', Enumerable.Repeat("аааааааааа", 8)) + ".com";
        var parsed = TalkActionParser.Parse($"[OPEN:https://{host}/{new string('p', TalkActionParser.MaxUrlLength)}]").Actions;
        Assert.Empty(parsed); // over the cap: dropped
        var url = Assert.Single(TalkActionParser.Parse($"[OPEN:https://{host}/{new string('p', 1500)}]").Actions).Value;
        Assert.True(TalkActionText.Opened(url).Body!.Length <= 2000);
        Assert.Contains("xn--", TalkActionText.Opened(url).Body);
    }

    [Fact]
    public void TypeNoticesUseTalkWording()
    {
        Assert.Contains("password", TalkActionText.TypeRefused("Dictation is unavailable in password or protected fields."));
        Assert.Contains("excluded", TalkActionText.TypeRefused("This app is excluded from dictation. Nothing was inspected."));
        Assert.Contains("read-only", TalkActionText.TypeRefused("The selected text field is read-only."));
        string[] notices =
        [
            TalkActionText.TypeRefused("Select an editable text field in another app, then start dictation again."),
            TalkActionText.TypeResult(true, true, true, true, 5), TalkActionText.TypeResult(false, false, false, false, 5),
            TalkActionText.TypeResult(true, false, false, false, 5), TalkActionText.TypeResult(true, true, false, false, 5),
            TalkActionText.TypeResult(true, true, false, true, 5),
        ];
        Assert.Equal(notices.Length, notices.Distinct().Count());
        Assert.All(notices, n => { Assert.DoesNotContain("dictat", n, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("draft", n); });
    }

    [Fact]
    public void TaskHeldTypeCardExplainsWhy()
    {
        var card = TalkActionText.TypeHeldForTask(new(TalkActionKind.Type, "a\tb"));
        Assert.Contains("task is running", card.Title);
        Assert.Equal("a b", card.Body);
        Assert.Empty(card.Actions);
    }

    [Fact]
    public void ConfirmationShowsTheFullAddressOrText()
    {
        var text = new string('t', 2000);
        Assert.EndsWith(text, TalkActionText.ConfirmSummary(new(TalkActionKind.Type, text), "Message in Notepad"));
        Assert.Contains("Message in Notepad", TalkActionText.ConfirmSummary(new(TalkActionKind.Type, text), "Message in Notepad"));
        Assert.EndsWith("https://example.com/x?y=1", TalkActionText.ConfirmSummary(new(TalkActionKind.Open, "https://example.com/x?y=1")));
    }
}
