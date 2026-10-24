using OpenClicky.Core.Shell;

namespace OpenClicky.Core;

/// <summary>Pure helpers for running talk actions: the text that is typed, the copy undo guard, the notices and the notch cards.</summary>
public static class TalkActionText
{
    public const string CopyCardId = "talk-copy", UndoAction = "undo";
    private const int CardMs = 15_000;

    /// <summary>Typing must not press Enter or Tab in someone's field, so every control character (CR, LF, tab, ...) becomes a space.</summary>
    public static string SanitizeForTyping(string text) => string.Create(text.Length, text, static (span, source) =>
    {
        for (var i = 0; i < source.Length; i++) span[i] = char.IsControl(source[i]) ? ' ' : source[i];
    });

    /// <summary>Undo puts the old text back only while the clipboard still holds exactly what we copied; anything newer is the user's.</summary>
    public static bool CanUndoCopy(string? clipboardNow, string copied) => string.Equals(clipboardNow, copied, StringComparison.Ordinal);

    /// <summary>The first 60 characters on one line, with an ellipsis when cut; never splits a surrogate pair.</summary>
    public static string Snippet(string text)
    {
        var line = SanitizeForTyping(text).Trim();
        if (line.Length <= 60) return line;
        var cut = char.IsHighSurrogate(line[59]) ? 59 : 60;
        return line[..cut] + "…";
    }

    /// <summary>The address as shown, plus the punycode host when the host has non-ASCII letters (lookalike domains).
    /// IP literals have no letters to fake (IdnHost drops an IPv6 literal's brackets, which is not a difference).</summary>
    public static string DescribeUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.HostNameType == UriHostNameType.Dns
        && !string.Equals(uri.IdnHost, uri.Host, StringComparison.OrdinalIgnoreCase)
            ? url + "\r\n(the site's real address is " + uri.IdnHost + ")"
            : url;

    public static NotchCard Copied(string text) => new(CopyCardId, NotchCardKind.Clipboard, "Copied: " + Snippet(text), null,
        [new CardAction(UndoAction, "Undo", CardActionStyle.Secondary)], CardMs);

    /// <summary>Shown when a page opened without asking (Auto or Full access), so the full address is always visible.</summary>
    public static NotchCard Opened(string url) => new("talk-open-" + Guid.NewGuid().ToString("N"), NotchCardKind.TextResponse,
        "Opened a page", DescribeUrl(url), [], CardMs);

    /// <summary>Preview access: exactly what would have happened, nothing executed.</summary>
    public static NotchCard Preview(TalkAction action) => new("talk-preview-" + Guid.NewGuid().ToString("N"), NotchCardKind.TextResponse,
        action.Kind switch
        {
            TalkActionKind.Open => "Preview: I would open this page",
            TalkActionKind.Copy => "Preview: I would copy this",
            _ => "Preview: I would type this into your field",
        },
        Body(action), [], CardMs);

    /// <summary>A talk TYPE held back because an agent task is running and may be typing too.</summary>
    public static NotchCard TypeHeldForTask(TalkAction action) => new("talk-preview-" + Guid.NewGuid().ToString("N"), NotchCardKind.TextResponse,
        "I didn't type this because a task is running", Body(action), [], CardMs);

    /// <summary>The confirmation text for an AskFirst action: the full address or the full text.</summary>
    public static string ConfirmSummary(TalkAction action, string? destination = null) => action.Kind == TalkActionKind.Open
        ? "Open this page in your browser:\r\n\r\n" + DescribeUrl(action.Value)
        : $"Type this into {destination ?? "the focused field"}:\r\n\r\n" + SanitizeForTyping(action.Value);

    /// <summary>Talk wording for a field the dictation target refused (its own messages talk about dictation).</summary>
    public static string TypeRefused(string reason) =>
        reason.Contains("password", StringComparison.OrdinalIgnoreCase) ? "I didn't type it because that's a password or protected field."
        : reason.Contains("excluded", StringComparison.OrdinalIgnoreCase) ? "I didn't type it because that app is excluded in your settings."
        : reason.Contains("read-only", StringComparison.OrdinalIgnoreCase) ? "I didn't type it because that field is read-only."
        : "I didn't type it. Click a text field in another app, then ask again.";

    /// <summary>Talk wording for the outcome of the verified insertion.</summary>
    public static string TypeResult(bool attempted, bool completed, bool verified, bool readable, int length) =>
        verified ? $"Typed {length} characters and checked they landed in the field."
        : !attempted ? "I didn't type it because the field changed. Click it and ask again."
        : !completed ? "Typing didn't finish. The field may hold part of the text."
        : !readable ? $"Typed {length} characters. That field can't be read back, so I couldn't check it."
        : "I typed it, but the field doesn't show it as a new change. Please check it.";

    private static string Body(TalkAction action) => action.Kind switch
    {
        TalkActionKind.Open => DescribeUrl(action.Value),
        TalkActionKind.Type => SanitizeForTyping(action.Value),
        _ => action.Value,
    };
}
