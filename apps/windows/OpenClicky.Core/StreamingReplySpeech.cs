namespace OpenClicky.Core;

internal sealed class StreamingReplySpeech
{
    private int _spokenLength;

    public string TakeCompleted(string cleanText)
    {
        if (cleanText.Length <= _spokenLength) return "";
        var end = -1;
        for (var i = _spokenLength; i < cleanText.Length; i++)
            if (cleanText[i] is '.' or '!' or '?' && (i + 1 == cleanText.Length || char.IsWhiteSpace(cleanText[i + 1]))) end = i + 1;
        return end < 0 ? "" : Take(cleanText, end);
    }

    public string TakeRemainder(string finalText) =>
        finalText.Length <= _spokenLength ? "" : Take(finalText, finalText.Length);

    private string Take(string text, int end)
    {
        var result = text[_spokenLength..end].Trim();
        _spokenLength = end;
        return result;
    }

    public static string Clean(string raw)
    {
        var text = ScreenAnnotations.Parse(VisualArtifactReply.Parse(IntelligentUiParser.Parse(raw).PlainText).PlainText).Text;
        var command = text.LastIndexOf('[');
        if (command >= 0 && text.IndexOf(']', command) < 0) text = text[..command];
        return PointParser.Parse(text).SpokenText.Trim();
    }
}
