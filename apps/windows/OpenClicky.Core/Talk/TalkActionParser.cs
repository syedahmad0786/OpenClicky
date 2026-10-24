using System.Text;

namespace OpenClicky.Core;

public enum TalkActionKind { Open, Copy, Type }

public sealed record TalkAction(TalkActionKind Kind, string Value);

public sealed record TalkActionParseResult(string Text, IReadOnlyList<TalkAction> Actions);

/// <summary>
/// Extracts [OPEN:url], [COPY:text] and [TYPE:text] tags from a talk reply. Tags are always removed from the
/// returned text; only valid ones (first three, in order) become actions. Fenced code blocks are left untouched
/// because the ag-ui visual block lives there. Run this before the POINT/MARK/ag-ui parsers.
/// </summary>
public static class TalkActionParser
{
    public const int MaxActions = 3;
    public const int MaxTextLength = 2000;
    /// <summary>Leaves room in a 2,000-char card body for the punycode host line of a lookalike domain.</summary>
    public const int MaxUrlLength = 1700;

    private static readonly (string Prefix, TalkActionKind Kind)[] Tags =
        [("[OPEN:", TalkActionKind.Open), ("[COPY:", TalkActionKind.Copy), ("[TYPE:", TalkActionKind.Type)];

    /// <param name="partial">Streaming text: an unclosed tag is cut off (so it is never spoken) instead of kept as text.</param>
    public static TalkActionParseResult Parse(string reply, bool partial = false)
    {
        ArgumentNullException.ThrowIfNull(reply);
        var text = new StringBuilder(reply.Length);
        var actions = new List<TalkAction>();
        var removed = false;
        var inFence = false;
        for (var i = 0; i < reply.Length;)
        {
            if (string.CompareOrdinal(reply, i, "```", 0, 3) == 0)
            {
                inFence = !inFence;
                text.Append("```");
                i += 3;
                continue;
            }
            var tag = inFence || reply[i] != '[' ? -1 : Array.FindIndex(Tags, t => string.CompareOrdinal(reply, i, t.Prefix, 0, t.Prefix.Length) == 0);
            if (tag < 0)
            {
                text.Append(reply[i++]);
                continue;
            }
            var end = FindClose(reply, i + Tags[tag].Prefix.Length, out var value);
            if (end < 0)
            {
                // No later tag can close either (its value scan is a suffix of this one), so stop: the scan stays linear.
                if (partial) removed = true;
                else text.Append(reply, i, reply.Length - i);
                break;
            }
            removed = true;
            if (actions.Count < MaxActions && Validate(Tags[tag].Kind, value.Trim()) is { } action) actions.Add(action);
            i = end;
            // Drop the gap the tag leaves behind so "a [COPY:x] b" reads "a b".
            if (i < reply.Length && reply[i] is (char)13 or (char)10)
                while (text.Length > 0 && text[^1] is ' ' or (char)9) text.Length--;
            else if (text.Length == 0 || char.IsWhiteSpace(text[^1]))
                while (i < reply.Length && reply[i] is ' ' or (char)9) i++;
        }
        return new(removed ? text.ToString().Trim() : reply, actions);
    }

    /// <summary>Index just past the closing ']', or -1 when unclosed. In the value \] is a literal ']' and \\ a literal backslash.</summary>
    private static int FindClose(string s, int start, out string value)
    {
        var sb = new StringBuilder();
        for (var j = start; j < s.Length; j++)
        {
            if (s[j] == '\\' && j + 1 < s.Length && s[j + 1] is ']' or '\\') sb.Append(s[++j]);
            else if (s[j] == ']') { value = sb.ToString(); return j + 1; }
            else sb.Append(s[j]);
        }
        value = "";
        return -1;
    }

    private static TalkAction? Validate(TalkActionKind kind, string value)
    {
        if (value.Length == 0) return null;
        // A TYPE of only control characters would type nothing but spaces.
        if (kind == TalkActionKind.Type && string.IsNullOrWhiteSpace(TalkActionText.SanitizeForTyping(value))) return null;
        if (kind != TalkActionKind.Open) return value.Length <= MaxTextLength ? new(kind, value) : null;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0 && uri.AbsoluteUri.Length <= MaxUrlLength
            ? new(kind, uri.AbsoluteUri) : null;
    }
}
