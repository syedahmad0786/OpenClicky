using System.Text.RegularExpressions;

namespace OpenClicky.Core.Dictation;

/// <summary>Fits dictated text into the gap at the caret: spacing at both seams, single-line folding and em dashes.</summary>
public static class DictationTextFit
{
    public static string Fit(string text, string before, string after, bool singleLine)
    {
        text = (text ?? "").Trim();
        before ??= "";
        after ??= "";
        if (singleLine) text = Regex.Replace(text, @"[ \t]*[\r\n]+[ \t]*", " ");
        text = text.Replace(" -- ", " — ");

        if (text.StartsWith(',') && StartsSentence(before)) text = text[1..].TrimStart();
        if (text.Length == 0) return "";

        var leading = before.Length > 0
            && !char.IsWhiteSpace(before[^1])
            && "([{“‘".IndexOf(before[^1]) < 0
            && ",.;:!?)]}".IndexOf(text[0]) < 0
            && !(IsNoSpaceScript(before[^1]) && IsNoSpaceScript(text[0]));
        var trailing = after.Length > 0
            && char.IsLetterOrDigit(after[0])
            && !(IsNoSpaceScript(text[^1]) && IsNoSpaceScript(after[0]));
        return (leading ? " " : "") + text + (trailing ? " " : "");
    }

    private static bool StartsSentence(string before)
    {
        var b = before.TrimEnd(' ', '\t');
        return b.Length == 0 || ".!?\n\r".IndexOf(b[^1]) >= 0;
    }

    // ponytail: BMP ranges only (CJK ideographs, kana, Thai, CJK and fullwidth punctuation); add surrogate-pair ranges if Ext-B text shows up.
    internal static bool IsNoSpaceScript(char c) =>
        c is (>= '　' and <= 'ヿ') or (>= '㐀' and <= '䶿') or (>= '一' and <= '鿿')
            or (>= '฀' and <= '๿') or (>= '豈' and <= '﫿') or (>= '＀' and <= '￯');
}
