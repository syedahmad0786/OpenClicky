using OpenClicky.Core.Drawing;

namespace OpenClicky.Core;

internal sealed class StreamingReplySpeech
{
    private int _spokenLength;

    public string TakeCompleted(string cleanText)
    {
        if (cleanText.Length <= _spokenLength) return "";
        var end = -1;
        for (var i = _spokenLength; i < cleanText.Length; i++)
            if (IsSentenceEnd(cleanText, i)) end = i + 1;
        return end < 0 ? "" : Take(cleanText, end);
    }

    /// <summary>Sentence ends in already-cleaned text, by the same rule <see cref="TakeCompleted"/> splits on.</summary>
    internal static int CountSentenceEnds(string clean)
    {
        var count = 0;
        for (var i = 0; i < clean.Length; i++)
            if (IsSentenceEnd(clean, i)) count++;
        return count;
    }

    private static bool IsSentenceEnd(string text, int i) =>
        text[i] is '.' or '!' or '?' && (i + 1 == text.Length || char.IsWhiteSpace(text[i + 1]));

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
        var text = ScreenAnnotations.Parse(VisualArtifactReply.Parse(IntelligentUiParser.Parse(DrawCommandParser.Parse(TalkActionParser.Parse(raw, partial: true).Text, partial: true).Text).PlainText).PlainText).Text;
        var command = text.LastIndexOf('[');
        if (command >= 0 && text.IndexOf(']', command) < 0) text = text[..command];
        return PointParser.Parse(text).SpokenText.Trim();
    }
}
