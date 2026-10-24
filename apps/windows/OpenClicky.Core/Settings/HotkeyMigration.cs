using OpenClicky.Core.Shell;

namespace OpenClicky.Core.Settings;

/// <summary>One-time move from the old push-to-talk chord to <see cref="AppSettings.TalkHotkey"/>.</summary>
public static class HotkeyMigration
{
    public const int CurrentVersion = 2;

    /// <summary>
    /// A customised <see cref="AppSettings.PushToTalk"/> (≠ Win+Alt+Space) becomes a Hold talk binding of the same keys,
    /// so the user keeps a working shortcut; the default chord leaves the new default (hold Ctrl+Alt). A chord with a
    /// key shortcuts cannot use (Enter, Tab…) keeps the default. Returns true when settings changed and need saving.
    /// </summary>
    public static bool Apply(AppSettings s)
    {
        if (s.ShortcutsVersion >= CurrentVersion) return false;
        if (s.PushToTalk is { } chord && chord != HotkeyChord.Default
            && HotkeyBinding.TryCreate(chord.DisplayName.Split('+'), HotkeyTrigger.Hold, out var talk, out _))
            s.TalkHotkey = talk!;
        s.ShortcutsVersion = CurrentVersion;
        return true;
    }
}
