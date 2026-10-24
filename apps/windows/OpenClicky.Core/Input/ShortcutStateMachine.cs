using OpenClicky.Core.Settings;
using OpenClicky.Core.Shell;

namespace OpenClicky.Core.Input;

public enum ShortcutAction { Talk, Text, Dictate, HandsFree }

public enum ShortcutCommandKind
{
    TalkStart, TalkEnd, TextMode, DictateStart, DictateEnd, HandsFreeToggle, Cancel,
    /// <summary>Send an unassigned key (VK 0xE8) before the current Win key-up passes so the Start menu stays shut.</summary>
    MaskStartMenu,
    /// <summary>The recorder finished; <see cref="ShortcutCommand.Binding"/> is null on Esc or timeout.</summary>
    RecordingFinished,
}

/// <param name="Cancelled">For TalkEnd/DictateEnd: the hold was a tap, was interrupted by another key, or Esc.</param>
public sealed record ShortcutCommand(ShortcutCommandKind Kind, bool Cancelled = false, HotkeyBinding? Binding = null);

public sealed record ShortcutBindings(HotkeyBinding Talk, HotkeyBinding Text, HotkeyBinding Dictate, HotkeyBinding HandsFree)
{
    public static ShortcutBindings Default => new(HotkeyBinding.DefaultTalk, HotkeyBinding.DefaultText, HotkeyBinding.DefaultDictate, HotkeyBinding.DefaultHandsFree);

    public static ShortcutBindings From(AppSettings s) => new(
        HotkeyBinding.OrDefault(s.TalkHotkey, HotkeyBinding.DefaultTalk),
        HotkeyBinding.OrDefault(s.TextHotkey, HotkeyBinding.DefaultText),
        HotkeyBinding.OrDefault(s.DictateHotkey, HotkeyBinding.DefaultDictate),
        HotkeyBinding.OrDefault(s.HandsFreeHotkey, HotkeyBinding.DefaultHandsFree));
}

/// <summary>
/// Turns low-level key events into shortcut commands. Pure: no clock and no Win32; the host passes timestamps and
/// calls <see cref="Tick"/> while <see cref="NeedsTick"/> (the 150 ms hold delay and the recorder timeout).
/// <para>Rules. Left/right modifiers fold to Ctrl/Alt/Shift/Win. A binding is eligible only while the pressed set
/// equals its key set exactly and no other key has gone down since the keyboard was last fully released (any other
/// key poisons it until then), so Ctrl+C, Ctrl+Alt+Del and AltGr+letter never trigger it. A modifier-only hold
/// starts after 150 ms; a hold with a key starts on the spot. Releasing within 400 ms of the exact set going down
/// is a tap: the hold ends cancelled. Double-tap fires on the second release when both taps are taps and the second
/// goes down within 500 ms of the first's release. Esc cancels only an active action. Injected input is ignored.</para>
/// </summary>
public sealed class ShortcutStateMachine
{
    public const long HoldDelayMs = 150, TapMaxMs = 400, DoubleTapGapMs = 500, RecordTimeoutMs = 7000;
    private const int VkEscape = 0x1B;

    private readonly HashSet<int> _pressed = [];
    private readonly HashSet<int> _swallowed = [];
    private readonly HashSet<string> _sessionKeys = new(StringComparer.OrdinalIgnoreCase);
    private ShortcutBindings _bindings = ShortcutBindings.Default;
    private Slot[] _slots = [];
    private bool _maskWin;
    private ShortcutAction? _recording;
    private long _recordingSince;
    private List<string> _recordMax = [];

    public ShortcutStateMachine(ShortcutBindings bindings) => Bindings = bindings;

    /// <summary>Setting this drops per-binding state; set it while no shortcut is held.</summary>
    public ShortcutBindings Bindings
    {
        get => _bindings;
        set
        {
            _bindings = value;
            _slots =
            [
                new(ShortcutAction.Talk, value.Talk), new(ShortcutAction.Text, value.Text),
                new(ShortcutAction.Dictate, value.Dictate), new(ShortcutAction.HandsFree, value.HandsFree),
            ];
        }
    }

    /// <summary>True when the host must block the last key event (the non-modifier key of an active hold, or any
    /// non-modifier key while recording). Modifiers are never swallowed.</summary>
    public bool SwallowLastKey { get; private set; }

    public bool NeedsTick => IsRecording || _slots.Any(s => s.Pending);
    public bool IsRecording => _recording is not null;
    public HotkeyBinding? Recorded { get; private set; }
    public bool RecordingTimedOut { get; private set; }

    /// <summary>Held keys the OS saw go down (not swallowed). The host may release any the OS reports up, which
    /// recovers key-ups the hook never sees (e.g. after Win+L locks the session).</summary>
    public IReadOnlyList<int> PressedPassedKeys => _pressed.Where(vk => !_swallowed.Contains(vk)).ToArray();

    public IReadOnlyList<ShortcutCommand> Tick(long nowMs)
    {
        var commands = new List<ShortcutCommand>();
        Advance(nowMs, commands);
        return commands;
    }

    public IReadOnlyList<ShortcutCommand> OnKey(int vk, bool down, bool injected, long nowMs)
    {
        SwallowLastKey = false;
        if (injected) return []; // the agent's own SendInput must never start, end or cancel anything
        var commands = new List<ShortcutCommand>();
        Advance(nowMs, commands);
        if (down && !_pressed.Add(vk))
        {
            SwallowLastKey = _swallowed.Contains(vk); // auto-repeat changes nothing
            return commands;
        }
        if (!down && !_pressed.Remove(vk)) return commands; // never saw it go down

        var key = KeyName(vk);
        if (down) _sessionKeys.Add(key);
        if (!down && key == "Win" && (_maskWin || IsRecording)) commands.Add(new(ShortcutCommandKind.MaskStartMenu));

        if (down && vk == VkEscape && _slots.Any(s => s.Held || s.On))
        {
            foreach (var slot in _slots.Where(s => s.Held || s.On)) Stop(slot, cancelled: true, commands);
            commands.Add(new(ShortcutCommandKind.Cancel));
        }

        var pressed = _pressed.Select(KeyName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var slot in _slots) Update(slot, pressed, nowMs, commands);

        var swallow = false;
        if (down && IsRecording)
        {
            swallow = !IsModifier(key);
            if (vk == VkEscape) FinishRecording(null, timedOut: false, commands);
            else if (pressed.Count > _recordMax.Count) _recordMax = [.. pressed];
        }
        else if (down && !IsModifier(key))
        {
            swallow = _slots.Any(s => s.Held && s.Contains(key));
        }
        if (swallow) _swallowed.Add(vk);
        SwallowLastKey = down ? swallow : _swallowed.Remove(vk);

        if (_pressed.Count == 0) EndSession(nowMs, commands);
        return commands;
    }

    /// <summary>Starts capturing a chord for <paramref name="action"/>; shortcuts do not fire until it finishes.</summary>
    public void BeginRecording(ShortcutAction action, long nowMs)
    {
        _recording = action;
        _recordingSince = nowMs;
        _recordMax = [];
        Recorded = null;
        RecordingTimedOut = false;
        ForgetTaps();
    }

    /// <summary>Forgets every key and ends active actions as cancelled (the hook is going away).</summary>
    public IReadOnlyList<ShortcutCommand> Reset()
    {
        var commands = new List<ShortcutCommand>();
        foreach (var slot in _slots.Where(s => s.Held || s.On)) Stop(slot, cancelled: true, commands);
        if (IsRecording) FinishRecording(null, timedOut: false, commands);
        _pressed.Clear();
        _swallowed.Clear();
        _sessionKeys.Clear();
        _maskWin = false;
        Bindings = _bindings;
        return commands;
    }

    /// <summary>Folded key name in <see cref="HotkeyBinding"/>'s vocabulary; keys outside it get "Esc" or "VkXX".</summary>
    public static string KeyName(int vk) => vk switch
    {
        0x10 or 0xA0 or 0xA1 => "Shift",
        0x11 or 0xA2 or 0xA3 => "Ctrl",
        0x12 or 0xA4 or 0xA5 => "Alt",
        0x5B or 0x5C => "Win",
        0x20 => "Space",
        VkEscape => "Esc",
        >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)vk).ToString(),
        >= 0x70 and <= 0x7B => $"F{vk - 0x6F}",
        _ => $"Vk{vk:X2}",
    };

    private static bool IsModifier(string key) => HotkeyBinding.Modifiers.Contains(key);

    private void Advance(long nowMs, List<ShortcutCommand> commands)
    {
        if (IsRecording && nowMs - _recordingSince >= RecordTimeoutMs) FinishRecording(null, timedOut: true, commands);
        if (IsRecording) return;
        foreach (var slot in _slots)
            if (slot.Pending && nowMs - slot.ExactSince >= HoldDelayMs) Start(slot, commands);
    }

    private void Update(Slot slot, HashSet<string> pressed, long nowMs, List<ShortcutCommand> commands)
    {
        var poisoned = _sessionKeys.Any(k => !slot.Contains(k));
        var exact = !poisoned && pressed.Count == slot.Binding.Keys.Count && pressed.All(slot.Contains);
        if (exact)
        {
            if (slot.ExactSince is not null) return;
            slot.ExactSince = nowMs;
            slot.FirstExact ??= nowMs;
            if (slot.Contains("Win")) _maskWin = true;
            if (slot.Binding.Trigger == HotkeyTrigger.Hold && !slot.ModifierOnly && !IsRecording) Start(slot, commands);
            return;
        }
        if (slot.Held) Stop(slot, cancelled: poisoned || nowMs - slot.ExactSince <= TapMaxMs, commands);
        slot.ExactSince = null;
    }

    private void EndSession(long nowMs, List<ShortcutCommand> commands)
    {
        foreach (var slot in _slots)
        {
            if (slot.Binding.Trigger == HotkeyTrigger.DoubleTap)
            {
                var tap = slot.FirstExact is { } downAt && nowMs - downAt <= TapMaxMs && _sessionKeys.All(slot.Contains);
                if (!tap) slot.LastTapUp = null;
                else if (slot.LastTapUp is { } lastUp && slot.FirstExact - lastUp <= DoubleTapGapMs && !IsRecording)
                {
                    slot.LastTapUp = null;
                    Fire(slot, commands);
                }
                else slot.LastTapUp = nowMs;
            }
            slot.FirstExact = null;
        }
        _sessionKeys.Clear();
        _maskWin = false;

        if (_recording is not { } action || _recordMax.Count == 0) return;
        var trigger = action is ShortcutAction.Text or ShortcutAction.HandsFree ? HotkeyTrigger.DoubleTap : HotkeyTrigger.Hold;
        if (HotkeyBinding.TryCreate(_recordMax, trigger, out var binding, out _)) FinishRecording(binding, timedOut: false, commands);
        else _recordMax = []; // not a usable shortcut (e.g. a bare letter): keep listening until Esc or timeout
    }

    private void FinishRecording(HotkeyBinding? binding, bool timedOut, List<ShortcutCommand> commands)
    {
        _recording = null;
        _recordMax = [];
        Recorded = binding;
        RecordingTimedOut = timedOut;
        ForgetTaps();
        commands.Add(new(ShortcutCommandKind.RecordingFinished, Binding: binding));
    }

    private void ForgetTaps()
    {
        foreach (var slot in _slots) slot.ExactSince = slot.FirstExact = slot.LastTapUp = null;
    }

    private static void Start(Slot slot, List<ShortcutCommand> commands)
    {
        slot.Held = true;
        commands.Add(new(slot.StartKind));
    }

    private static void Stop(Slot slot, bool cancelled, List<ShortcutCommand> commands)
    {
        slot.Held = slot.On = false;
        if (slot.EndKind is { } end) commands.Add(new(end, cancelled));
    }

    /// <summary>Double-tap: Text/HandsFree fire once; Talk/Dictate toggle start and end.</summary>
    private static void Fire(Slot slot, List<ShortcutCommand> commands)
    {
        if (slot.On) Stop(slot, cancelled: false, commands);
        else
        {
            slot.On = slot.EndKind is not null;
            commands.Add(new(slot.StartKind));
        }
    }

    private sealed class Slot(ShortcutAction action, HotkeyBinding binding)
    {
        public HotkeyBinding Binding { get; } = binding;
        public bool ModifierOnly { get; } = binding.Keys.All(IsModifier);
        public ShortcutCommandKind StartKind { get; } = action switch
        {
            ShortcutAction.Talk => ShortcutCommandKind.TalkStart,
            ShortcutAction.Dictate => ShortcutCommandKind.DictateStart,
            ShortcutAction.Text => ShortcutCommandKind.TextMode,
            _ => ShortcutCommandKind.HandsFreeToggle,
        };
        public ShortcutCommandKind? EndKind { get; } = action switch
        {
            ShortcutAction.Talk => ShortcutCommandKind.TalkEnd,
            ShortcutAction.Dictate => ShortcutCommandKind.DictateEnd,
            _ => null,
        };

        /// <summary>When the pressed set last became exactly this binding (null while it is not).</summary>
        public long? ExactSince;
        /// <summary>First time this session the set was exact: the tap's key-down.</summary>
        public long? FirstExact;
        public long? LastTapUp;
        /// <summary>A hold is running.</summary>
        public bool Held;
        /// <summary>A double-tap toggle (talk/dictate) is on.</summary>
        public bool On;

        public bool Pending => Binding.Trigger == HotkeyTrigger.Hold && ModifierOnly && !Held && ExactSince is not null;
        public bool Contains(string key) => Binding.Keys.Contains(key, StringComparer.OrdinalIgnoreCase);
    }
}
