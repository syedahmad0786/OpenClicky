using OpenClicky.Core.Shell;

namespace OpenClicky.Core.Dictation;

/// <summary>Dictation's notch cards. None shows the dictated text, except the live transcript on the activity card.</summary>
public static class DictationCards
{
    public const string ClipboardId = "dictation-clipboard", ActivityId = "dictation-activity", LearnedId = "dictionary-learned";
    public const string StopAction = "stop", UndoAction = "undo";
    public const int ClipboardMs = 8000, LearnedMs = 6000, LiveChars = 160;

    /// <summary>The words could not be typed (or verified) in a field, so they went to the clipboard.</summary>
    public static NotchCard Clipboard() => new(ClipboardId, NotchCardKind.Clipboard, "Your words are on the clipboard",
        "Click where they should go and press Ctrl+V.", [], ClipboardMs);

    /// <summary>Shown while dictating; hands-free gets Stop (a hold ends on release). The body is the live transcript's tail.</summary>
    public static NotchCard Activity(bool handsFree, string? liveText) => new(ActivityId, NotchCardKind.Activity,
        handsFree ? "Hands-free dictation" : "Dictating", Tail(liveText),
        handsFree ? [new(StopAction, "Stop", CardActionStyle.Danger)] : []);

    public static NotchCard Learned(string word) => new(LearnedId, NotchCardKind.DictionaryLearned, "New dictionary word", word,
        [new(UndoAction, "Undo", CardActionStyle.Secondary)], LearnedMs);

    private static string? Tail(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (text.Length <= LiveChars) return text;
        var start = text.Length - LiveChars;
        if (char.IsLowSurrogate(text[start])) start++; // never start on half of a pair
        return "…" + text[start..];
    }
}
