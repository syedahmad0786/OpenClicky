using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using OpenClicky.Core.Shell;

namespace OpenClicky.Core.Settings;

/// <summary>
/// A persisted shortcut: a set of keys plus how it is triggered. Keys are canonical ("Ctrl", "Alt", "Shift", "Win",
/// then at most one of A-Z, 0-9, Space, F1-F12), so equality of key sets is a simple case-insensitive set compare.
/// </summary>
public sealed record HotkeyBinding(IReadOnlyList<string> Keys, HotkeyTrigger Trigger)
{
    public static readonly IReadOnlyList<string> Modifiers = ["Ctrl", "Alt", "Shift", "Win"];
    private static readonly Regex FunctionKey = new(@"^[fF]([1-9]|1[0-2])\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    public static HotkeyBinding DefaultTalk => new(["Ctrl", "Alt"], HotkeyTrigger.Hold);
    public static HotkeyBinding DefaultText => new(["Ctrl"], HotkeyTrigger.DoubleTap);
    public static HotkeyBinding DefaultDictate => new(["Ctrl", "Win"], HotkeyTrigger.Hold);
    public static HotkeyBinding DefaultHandsFree => new(["Ctrl", "Win"], HotkeyTrigger.DoubleTap);

    [JsonIgnore] public string DisplayName => string.Join("+", Keys);

    /// <summary>"Hold Ctrl+Alt" or "Double-tap Ctrl".</summary>
    [JsonIgnore] public string Gesture => (Trigger == HotkeyTrigger.Hold ? "Hold " : "Double-tap ") + DisplayName;

    public Hotkey ToView() => new(Keys.Take(5).ToList(), Trigger);

    /// <summary>Same trigger and the same keys in any order.</summary>
    public bool SameChord(HotkeyBinding other) =>
        Trigger == other.Trigger && Keys.Count == other.Keys.Count
        && Keys.All(k => other.Keys.Contains(k, StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Validates and canonicalises. Rules: 1-5 keys, no repeats, modifiers (Ctrl, Alt, Shift, Win) plus at most one
    /// key from A-Z, 0-9, Space, F1-F12; a letter, digit or Space needs a modifier so typing is not stolen;
    /// double-tap needs exactly one key or a modifier-only set.
    /// </summary>
    public static bool TryCreate(IEnumerable<string?> keys, HotkeyTrigger trigger, out HotkeyBinding? binding, out string? error)
    {
        binding = null;
        var tokens = keys.Take(6).ToList();
        if (tokens.Count is < 1 or > 5) return Fail("A shortcut needs between one and five keys.", out error);

        var canonical = new List<string>();
        foreach (var token in tokens)
        {
            if (Canonical(token) is not { } key) return Fail("That key is not supported in shortcuts.", out error);
            if (canonical.Contains(key)) return Fail("A key can only appear once in a shortcut.", out error);
            canonical.Add(key);
        }

        var modifiers = Modifiers.Where(canonical.Contains).ToList();
        var others = canonical.Where(k => !Modifiers.Contains(k)).ToList();
        if (others.Count > 1) return Fail("A shortcut can use only one non-modifier key.", out error);
        if (others.Count == 1 && modifiers.Count == 0 && !others[0].StartsWith('F'))
            return Fail("Add a modifier (Ctrl, Alt, Shift or Win) so the shortcut does not interfere with typing.", out error);
        if (trigger == HotkeyTrigger.DoubleTap && canonical.Count != 1 && others.Count != 0)
            return Fail("Double-tap works with a single key or with modifier keys only.", out error);

        binding = new HotkeyBinding([.. modifiers, .. others], trigger);
        error = null;
        return true;
    }

    /// <summary>The binding itself when valid, else <paramref name="fallback"/> (hand-edited settings.json).</summary>
    public static HotkeyBinding OrDefault(HotkeyBinding? binding, HotkeyBinding fallback) =>
        binding?.Keys is { } keys && TryCreate(keys, binding.Trigger, out var ok, out _) ? ok! : fallback;

    private static string? Canonical(string? token)
    {
        var t = token?.Trim();
        if (string.IsNullOrEmpty(t)) return null;
        switch (t.ToLowerInvariant())
        {
            case "ctrl" or "control": return "Ctrl";
            case "alt": return "Alt";
            case "shift": return "Shift";
            case "win" or "windows": return "Win";
            case "space": return "Space";
        }
        if (t.Length == 1 && (t[0] is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9')) return t.ToUpperInvariant();
        return FunctionKey.IsMatch(t) ? "F" + t[1..] : null;
    }

    private static bool Fail(string message, out string? error)
    {
        error = message;
        return false;
    }
}
