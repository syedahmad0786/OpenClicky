namespace OpenClicky.Core;

/// <summary>
/// Remappable push-to-talk chord. Default is Win+Alt+Space.
/// </summary>
public sealed record HotkeyChord(bool Win, bool Alt, bool Control, bool Shift, int VirtualKey)
{
    public static HotkeyChord Default { get; } = new(Win: true, Alt: true, Control: false, Shift: false, VirtualKey: 0x20); // VK_SPACE

    /// <summary>True when the chord has a non-modifier key plus at least one modifier.</summary>
    public bool IsValid => TryValidate(out _);

    public string DisplayName
    {
        get
        {
            if (VirtualKey == 0 && !Win && !Alt && !Control && !Shift)
            {
                return "(not set)";
            }

            var parts = new List<string>();
            if (Win)
            {
                parts.Add("Win");
            }

            if (Control)
            {
                parts.Add("Ctrl");
            }

            if (Alt)
            {
                parts.Add("Alt");
            }

            if (Shift)
            {
                parts.Add("Shift");
            }

            parts.Add(VirtualKeyName(VirtualKey));
            return string.Join("+", parts);
        }
    }

    public bool Matches(bool winDown, bool altDown, bool controlDown, bool shiftDown, int virtualKey) =>
        Win == winDown
        && Alt == altDown
        && Control == controlDown
        && Shift == shiftDown
        && VirtualKey == virtualKey;

    /// <summary>
    /// Validates a remappable PTT binding. Bare keys (no modifiers) and modifier-only
    /// / empty chords are rejected so typing is not stolen and Save cannot persist junk.
    /// </summary>
    public bool TryValidate(out string? error)
    {
        if (VirtualKey == 0)
        {
            error = "Hotkey is empty. Click the field and press a shortcut (e.g. Win+Alt+Space).";
            return false;
        }

        if (IsModifierVirtualKey(VirtualKey))
        {
            error = "Hotkey cannot be only a modifier key. Add a non-modifier key (e.g. Space or a letter).";
            return false;
        }

        if (!Win && !Alt && !Control && !Shift)
        {
            error = "Hotkey needs at least one modifier (Win, Alt, Ctrl, or Shift) so it does not conflict with normal typing.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>Returns <paramref name="chord"/> when valid; otherwise <see cref="Default"/>.</summary>
    public static HotkeyChord NormalizeOrDefault(HotkeyChord? chord) =>
        chord is { } c && c.IsValid ? c : Default;

    public static bool IsModifierVirtualKey(int virtualKey) => virtualKey is
        0x10 or 0xA0 or 0xA1 // VK_SHIFT / L / R
        or 0x11 or 0xA2 or 0xA3 // VK_CONTROL / L / R
        or 0x12 or 0xA4 or 0xA5 // VK_MENU / L / R
        or 0x5B or 0x5C; // VK_LWIN / VK_RWIN

    private static string VirtualKeyName(int virtualKey) => virtualKey switch
    {
        0 => "?",
        0x20 => "Space",
        0x1B => "Esc",
        0x0D => "Enter",
        0x09 => "Tab",
        >= 0x30 and <= 0x39 => ((char)virtualKey).ToString(),
        >= 0x41 and <= 0x5A => ((char)virtualKey).ToString(),
        _ => $"Vk{virtualKey:X2}",
    };
}
