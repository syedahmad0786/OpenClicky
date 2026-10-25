using OpenClicky.Core.Dictation;
using OpenClicky.Core.Shell;

namespace OpenClicky.Core.Tests;

public class DictationCardsTests
{
    [Fact]
    public void ClipboardCardTellsTheUserToPaste()
    {
        var card = DictationCards.Clipboard();
        Assert.Equal(DictationCards.ClipboardId, card.Id);
        Assert.Equal(NotchCardKind.Clipboard, card.Kind);
        Assert.Equal("Your words are on the clipboard", card.Title);
        Assert.Equal("Click where they should go and press Ctrl+V.", card.Body);
        Assert.Equal(8000, card.AutoDismissMs);
        Assert.Empty(card.Actions);
    }

    [Fact]
    public void UnconfirmedClipboardCardWarnsBeforePasting()
    {
        var card = DictationCards.ClipboardUnconfirmed();
        Assert.Equal(DictationCards.ClipboardId, card.Id); // replaces the plain clipboard card, never stacks
        Assert.Equal(NotchCardKind.Clipboard, card.Kind);
        Assert.Equal("Check the field before pasting", card.Title);
        Assert.Equal("I typed your words but couldn't confirm them. They're also on the clipboard.", card.Body);
        Assert.Equal(8000, card.AutoDismissMs);
        Assert.Empty(card.Actions);
    }

    [Fact]
    public void TidyNoticeOffersTurnOffAndOk()
    {
        var card = DictationCards.TidyNotice();
        Assert.Equal("dictation-tidy-notice", card.Id);
        Assert.Equal(DictationCards.TidyNoticeId, card.Id);
        Assert.Equal(NotchCardKind.Tip, card.Kind);
        Assert.Equal("Tidy up with AI is on", card.Title);
        Assert.Equal("Dictated words, the app name and the text around the cursor go to your model provider to fix punctuation. Your voice stays on this PC.", card.Body);
        Assert.Equal(["off", "ok"], card.Actions.Select(a => a.Id));
        Assert.Equal(["Turn off", "OK"], card.Actions.Select(a => a.Label));
        Assert.Equal(DictationCards.TurnOffAction, card.Actions[0].Id);
        Assert.Equal(DictationCards.OkAction, card.Actions[1].Id);
        Assert.Null(card.AutoDismissMs); // stays until answered
    }

    [Fact]
    public void TidyNoticeFlagDefaultsOffAndPersists()
    {
        Assert.False(new AppSettings().DictationCleanupNoticeShown);
        var path = Path.Combine(Path.GetTempPath(), $"openclicky-notice-{Guid.NewGuid():N}.json");
        try
        {
            SettingsStore.Save(new AppSettings { DictationCleanupNoticeShown = true }, path);
            Assert.True(SettingsStore.Load(path).DictationCleanupNoticeShown);
        }
        finally { File.Delete(path); }
        // "Turn off" applies this patch.
        var s = new AppSettings();
        Assert.Equal(OpenClicky.Core.Settings.PatchResult.Applied, OpenClicky.Core.Settings.SettingsPatch.TryApply(s,
            DictationCards.TidyNoticeOffKey, System.Text.Json.JsonSerializer.SerializeToElement(false), out _));
        Assert.False(s.DictationCleanup);
    }

    [Fact]
    public void HeldActivityHasNoActions()
    {
        var card = DictationCards.Activity(false, null);
        Assert.Equal(DictationCards.ActivityId, card.Id);
        Assert.Equal(NotchCardKind.Activity, card.Kind);
        Assert.Equal("Dictating", card.Title);
        Assert.Null(card.Body);
        Assert.Empty(card.Actions);
        Assert.Null(card.AutoDismissMs);
    }

    [Fact]
    public void HandsFreeActivityShowsLiveTextAndStop()
    {
        var card = DictationCards.Activity(true, "hi");
        Assert.Equal("Hands-free dictation", card.Title);
        Assert.Equal("hi", card.Body);
        var stop = Assert.Single(card.Actions);
        Assert.Equal(DictationCards.StopAction, stop.Id);
        Assert.Equal("Stop", stop.Label);
    }

    [Fact]
    public void ActivityBodyKeepsTheLast160Characters()
    {
        var exact = new string('a', 160);
        Assert.Equal(exact, DictationCards.Activity(false, exact).Body);

        var tail = new string('b', 160);
        var body = DictationCards.Activity(false, "start " + tail).Body;
        Assert.Equal("…" + tail, body);
    }

    [Fact]
    public void ActivityBodyNeverStartsWithAHalfSurrogatePair()
    {
        var emoji = "\U0001F600"; // two UTF-16 units
        var text = "x" + emoji + new string('c', 159); // the 160-unit cut lands inside the pair
        var body = DictationCards.Activity(false, text).Body!;
        Assert.StartsWith("…", body);
        Assert.False(char.IsLowSurrogate(body[1]));
    }

    [Fact]
    public void LearnedCardOffersUndo()
    {
        var card = DictationCards.Learned("Kubernetes");
        Assert.Equal(DictationCards.LearnedId, card.Id);
        Assert.Equal(NotchCardKind.DictionaryLearned, card.Kind);
        Assert.Equal("New dictionary word", card.Title);
        Assert.Equal("Kubernetes", card.Body);
        var undo = Assert.Single(card.Actions);
        Assert.Equal(DictationCards.UndoAction, undo.Id);
        Assert.Equal(6000, card.AutoDismissMs);
    }

    [Fact]
    public void IdsAreFixed()
    {
        Assert.Equal("dictation-clipboard", DictationCards.ClipboardId);
        Assert.Equal("dictation-activity", DictationCards.ActivityId);
        Assert.Equal("dictionary-learned", DictationCards.LearnedId);
        Assert.Equal("stop", DictationCards.StopAction);
        Assert.Equal("undo", DictationCards.UndoAction);
    }
}
