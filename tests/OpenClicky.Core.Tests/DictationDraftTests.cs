using OpenClicky.Core;

namespace OpenClicky.Core.Tests;

public class DictationDraftTests
{
    [Fact]
    public void KeepsRawTranscriptSeparateAfterUserEditsDraft()
    {
        var draft = new DictationDraft();
        draft.AcceptTranscript("  raw words  ");
        draft.Edit("Edited words.");
        draft.AcceptTranscript("new raw words");

        Assert.Equal("new raw words", draft.RawTranscript);
        Assert.Equal("Edited words.", draft.Draft);
        Assert.True(draft.WasEdited);
    }

    [Fact]
    public void NewTranscriptUpdatesUntouchedDraftAndResetStartsClean()
    {
        var draft = new DictationDraft();
        draft.AcceptTranscript("first local result");
        Assert.Equal("first local result", draft.Draft);

        draft.Reset();
        draft.AcceptTranscript("second local result");
        Assert.Equal("second local result", draft.RawTranscript);
        Assert.Equal("second local result", draft.Draft);
        Assert.False(draft.WasEdited);
    }
}
