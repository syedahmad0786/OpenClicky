namespace OpenClicky.Core;

public sealed class DictationDraft
{
    public string RawTranscript { get; private set; } = "";
    public string Draft { get; private set; } = "";
    public bool WasEdited { get; private set; }

    public void Reset()
    {
        RawTranscript = "";
        Draft = "";
        WasEdited = false;
    }

    public void AcceptTranscript(string text)
    {
        RawTranscript = text.Trim();
        if (!WasEdited) Draft = RawTranscript;
    }

    public void Edit(string text)
    {
        Draft = text;
        WasEdited = !string.Equals(Draft, RawTranscript, StringComparison.Ordinal);
    }
}
