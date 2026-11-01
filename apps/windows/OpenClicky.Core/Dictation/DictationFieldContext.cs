namespace OpenClicky.Core.Dictation;

/// <summary>What surrounds the caret in the focused field. Untrusted screen text: callers quote it as data.</summary>
public sealed record DictationFieldContext(string AppName, string? SiteHost, string BeforeText, string AfterText, string SelectedText, bool SingleLine)
{
    /// <summary>
    /// Splits <paramref name="value"/> around the selection: up to <paramref name="cap"/> characters before it (the nearest ones),
    /// the selection (capped) and up to <paramref name="cap"/> after it. Out-of-range indices are clamped; pass the value's
    /// length as the start when the caret cannot be read, which puts it at the end.
    /// </summary>
    public static DictationFieldContext FromValue(string appName, string? siteHost, string value, int selectionStart, int selectionLength,
        bool singleLine, int cap = 2000)
    {
        value ??= "";
        var start = Math.Clamp(selectionStart, 0, value.Length);
        var length = Math.Clamp(selectionLength, 0, value.Length - start);
        var end = start + length;
        var beforeFrom = Math.Max(0, start - cap);
        return new(appName, siteHost,
            value[beforeFrom..start],
            value.Substring(end, Math.Min(cap, value.Length - end)),
            value.Substring(start, Math.Min(cap, length)),
            singleLine);
    }

    /// <summary>The lower-case host of an http(s) URL, or of bare address-bar text like <c>mail.google.com/x</c>; otherwise null.</summary>
    public static string? HostOf(string? addressBarText)
    {
        var text = addressBarText?.Trim();
        if (string.IsNullOrEmpty(text) || text.Any(char.IsWhiteSpace)) return null;
        if (!text.Contains("://")) text = "https://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return null;
        var host = uri.Host.ToLowerInvariant();
        return host.Contains('.') || host == "localhost" ? host : null;
    }
}
