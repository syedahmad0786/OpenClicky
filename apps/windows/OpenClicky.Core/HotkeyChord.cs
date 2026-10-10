namespace OpenClicky.Core;

/// <summary>
/// Remappable push-to-talk chord. Default is Win+Alt+Space.
/// </summary>
public sealed record HotkeyChord(bool Win, bool Alt, bool Control, bool Shift, int VirtualKey)
{
    public static HotkeyChord Default { get; } = new(Win: true, Alt: true, Control: false, Shift: false, VirtualKey: 0x20); // VK_SPACE

    public string DisplayName
    {
        get
        {
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

    private static string VirtualKeyName(int virtualKey) => virtualKey switch
    {
        0x20 => "Space",
        >= 0x30 and <= 0x39 => ((char)virtualKey).ToString(),
        >= 0x41 and <= 0x5A => ((char)virtualKey).ToString(),
        _ => $"Vk{virtualKey:X2}",
    };
}
