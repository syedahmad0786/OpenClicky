using OpenClicky.Core.Settings;

namespace OpenClicky.Core.CompanionCursor;

public enum CursorPresence { Following, Docked, Off }

/// <summary>What the companion overlay needs from settings, resolved once per apply.</summary>
public sealed record CompanionStyle(string AccentHex, CursorPresence Presence, bool ShakeToDetach, bool ScreenHighlight, bool ReducedMotion)
{
    public static CompanionStyle From(AppSettings s, bool reducedMotion) => new(
        SettingsCatalog.SnapAccent(s.AccentColor),
        s.Paused ? CursorPresence.Off : s.CursorHidden ? CursorPresence.Docked : CursorPresence.Following,
        s.ShakeToDetach,
        s.ScreenHighlight,
        reducedMotion);
}
