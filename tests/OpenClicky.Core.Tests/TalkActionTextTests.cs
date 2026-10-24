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
    public void ConfirmationShowsTheFullAddressOrText()
    {
        var text = new string('t', 2000);
        Assert.EndsWith(text, TalkActionText.ConfirmSummary(new(TalkActionKind.Type, text), "Message in Notepad"));
        Assert.Contains("Message in Notepad", TalkActionText.ConfirmSummary(new(TalkActionKind.Type, text), "Message in Notepad"));
        Assert.EndsWith("https://example.com/x?y=1", TalkActionText.ConfirmSummary(new(TalkActionKind.Open, "https://example.com/x?y=1")));
    }
}
