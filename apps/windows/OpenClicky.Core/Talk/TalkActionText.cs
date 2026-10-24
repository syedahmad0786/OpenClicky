using OpenClicky.Core.Shell;

namespace OpenClicky.Core;

/// <summary>Pure helpers for running talk actions: the text that is typed, the copy undo guard and the notch cards.</summary>
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

    /// <summary>The first 60 characters on one line, with an ellipsis when cut.</summary>
    public static string Snippet(string text)
    {
        var line = SanitizeForTyping(text).Trim();
        return line.Length <= 60 ? line : line[..60] + "…";
    }

    public static NotchCard Copied(string text) => new(CopyCardId, NotchCardKind.Clipboard, "Copied: " + Snippet(text), null,
        [new CardAction(UndoAction, "Undo", CardActionStyle.Secondary)], CardMs);

    /// <summary>Shown when a page opened without asking (Auto or Full access), so the full address is always visible.</summary>
    public static NotchCard Opened(string url) => new("talk-open-" + Guid.NewGuid().ToString("N"), NotchCardKind.TextResponse,
        "Opened a page", url, [], CardMs);

    /// <summary>Preview access: exactly what would have happened, nothing executed.</summary>
    public static NotchCard Preview(TalkAction action) => new("talk-preview-" + Guid.NewGuid().ToString("N"), NotchCardKind.TextResponse,
        action.Kind switch
        {
            TalkActionKind.Open => "Preview: I would open this page",
            TalkActionKind.Copy => "Preview: I would copy this",
            _ => "Preview: I would type this into your field",
        },
        action.Kind == TalkActionKind.Type ? SanitizeForTyping(action.Value) : action.Value, [], CardMs);

    /// <summary>The confirmation text for an AskFirst action: the full address or the full text.</summary>
    public static string ConfirmSummary(TalkAction action, string? destination = null) => action.Kind == TalkActionKind.Open
        ? "Open this page in your browser:\r\n\r\n" + action.Value
        : $"Type this into {destination ?? "the focused field"}:\r\n\r\n" + SanitizeForTyping(action.Value);
}
