using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenClicky.Core;
using OpenClicky.Core.Input;
using OpenClicky.Core.Settings;

namespace OpenClicky.Input;

/// <summary>
/// Hosts <see cref="ShortcutStateMachine"/> on a <c>WH_KEYBOARD_LL</c> hook (UI thread). Pressed/Released are talk;
/// Released carries <see cref="ShortcutEndedEventArgs"/>. Events are posted to the UI SynchronizationContext captured
/// in <see cref="Start"/> so handlers never run inside (and stall) the hook. Only a binding's non-modifier key (and
/// non-modifier keys while recording) is swallowed; injected input passes through.
/// Mouse clicks arrive through <see cref="NotifyMouseButton"/> (no mouse hook: it would put every mouse move behind the UI thread).
/// Unlike the old push-to-talk monitor, <see cref="Cancelled"/> fires on Esc only while an action is active;
/// <see cref="EscapePressed"/> fires on every physical Esc press (never swallowed).
/// </summary>
public sealed class ShortcutMonitor : IPushToTalkMonitor
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100, WmKeyUp = 0x0101, WmSysKeyDown = 0x0104, WmSysKeyUp = 0x0105;
    private const int LlkhfInjected = 0x10;
    private const ushort VkUnassignedMask = 0xE8;
    private const int VkEscape = 0x1B;

    private readonly NativeMethods.HookProc _keyboardProc;
    private readonly ShortcutStateMachine _machine;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };
    private SynchronizationContext? _context;
    private Action<HotkeyBinding?>? _recordDone;
    private IntPtr _keyboardHook;
    private ShortcutBindings? _pendingBindings;
    private bool _escapeDown;
    private bool _disposed;

    public ShortcutMonitor(ShortcutBindings? bindings = null)
    {
        _machine = new ShortcutStateMachine(bindings ?? ShortcutBindings.Default);
        _keyboardProc = KeyboardCallback; // keep the delegate alive for the native callback
        _timer.Tick += (_, _) =>
        {
            var now = Environment.TickCount64;
            DropMissedKeyUps(currentVk: -1, now); // a running hold must not outlive a key-up the hook never saw
            Run(_machine.Tick(now));
            if (!_machine.NeedsTick) _timer.Stop();
        };
    }

    /// <summary>Set on the UI thread. While a shortcut is pending, held or on, the new bindings wait until it ends.</summary>
    public ShortcutBindings Bindings
    {
        get => _pendingBindings ?? _machine.Bindings;
        set
        {
            _pendingBindings = value;
            ApplyPendingBindings();
        }
    }

    private void ApplyPendingBindings()
    {
        if (_pendingBindings is not { } bindings || !_machine.Idle) return;
        _pendingBindings = null;
        _machine.Bindings = bindings;
        if (_machine.NeedsTick) _timer.Start();
    }

    /// <summary>Ignored: shortcuts come from <see cref="Bindings"/>. Kept for <see cref="IPushToTalkMonitor"/>.</summary>
    public HotkeyChord Chord { get; set; } = HotkeyChord.Default;

    public event EventHandler? Pressed;
    public event EventHandler? Released;
    public event EventHandler? Cancelled;
    public event EventHandler? TextMode;
    public event EventHandler? DictateStarted;
    public event EventHandler<ShortcutEndedEventArgs>? DictateEnded;
    public event EventHandler? HandsFreeToggled;
    /// <summary>Every physical Esc press (not repeats), whether or not a shortcut is active. Esc is never swallowed.</summary>
    public event EventHandler? EscapePressed;

    public void Start()
    {
        if (_keyboardHook != IntPtr.Zero) return;
        _context = SynchronizationContext.Current;
        _keyboardHook = NativeMethods.SetWindowsHookEx(WhKeyboardLl, _keyboardProc, ResolveModuleHandle(), 0);
        if (_keyboardHook == IntPtr.Zero) throw new InvalidOperationException($"SetWindowsHookEx failed: {Marshal.GetLastWin32Error()}");
    }

    /// <summary>Unhooks and ends any active action as cancelled (and any recording with null).</summary>
    public void Stop()
    {
        if (_keyboardHook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_keyboardHook);
        _keyboardHook = IntPtr.Zero;
        _timer.Stop();
        Run(_machine.Reset());
    }

    /// <summary>
    /// Captures the next chord for <paramref name="action"/> (hold for talk/dictate, double-tap for text/hands-free).
    /// <paramref name="done"/> gets the binding, or null on Esc, 7 s timeout, Stop or a newer Record call.
    /// </summary>
    public void Record(ShortcutAction action, Action<HotkeyBinding?> done)
    {
        var previous = _recordDone;
        _recordDone = done;
        if (previous is not null) Post(() => previous(null));
        _machine.BeginRecording(action, Environment.TickCount64);
        _timer.Start();
    }

    private IntPtr KeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            var message = wParam.ToInt32();
            var down = message is WmKeyDown or WmSysKeyDown;
            if (nCode >= 0 && (down || message is WmKeyUp or WmSysKeyUp))
            {
                var data = Marshal.PtrToStructure<NativeMethods.KbdLlHookStruct>(lParam);
                if (HandleKey(data.VkCode, down, (data.Flags & LlkhfInjected) != 0, Environment.TickCount64)) return 1;
            }
        }
        catch (Exception ex)
        {
            Trace.TraceError($"ShortcutMonitor keyboard hook: {ex}"); // never break the user's keyboard
        }
        return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    /// <summary>One keyboard event from the hook; returns true when it must be swallowed.</summary>
    internal bool HandleKey(int vk, bool down, bool injected, long now)
    {
        if (!injected) DropMissedKeyUps(vk, now);
        if (!injected && vk == VkEscape)
        {
            if (down && !_escapeDown && !_machine.IsRecording) Post(() => EscapePressed?.Invoke(this, EventArgs.Empty));
            _escapeDown = down;
        }
        var commands = _machine.OnKey(vk, down, injected, now);
        var swallow = _machine.SwallowLastKey;
        Run(commands); // MaskStartMenu is sent here, before this Win key-up is passed on
        if (_machine.NeedsTick) _timer.Start();
        return swallow;
    }

    /// <summary>
    /// A physical (not injected) mouse button went down. Call on the UI thread, e.g. from the raw-input
    /// PhysicalUserActivityMonitor (an input sink that never blocks input). It poisons shortcuts that have not fired
    /// yet (Ctrl+click is not a Ctrl tap); a running hold keeps going.
    /// </summary>
    public void NotifyMouseButton()
    {
        Run(_machine.OnMouseButton(Environment.TickCount64));
        if (_machine.NeedsTick) _timer.Start();
    }

    /// <summary>The OS key state (tests replace it: their keys are not really held).</summary>
    internal Func<int, bool> IsKeyDown { get; set; } = vk => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>The hook misses key-ups on the secure desktop (UAC, Win+L, Ctrl+Alt+Del) and some focus changes;
    /// release any passed-through key the OS no longer reports down (what it held ends cancelled). Swallowed keys
    /// are covered inside the state machine by their auto-repeat going quiet (the OS never saw them go down).</summary>
    private void DropMissedKeyUps(int currentVk, long now)
    {
        foreach (var vk in _machine.PressedPassedKeys)
            if (vk != currentVk && !IsKeyDown(vk))
                Run(_machine.ReleaseMissed(vk, now));
    }

    private void Run(IReadOnlyList<ShortcutCommand> commands)
    {
        foreach (var command in commands)
        {
            if (command.Kind == ShortcutCommandKind.MaskStartMenu) SendStartMenuMask();
            else if (command.Kind == ShortcutCommandKind.RecordingFinished)
            {
                var done = _recordDone;
                _recordDone = null;
                if (done is not null) Post(() => done(command.Binding));
            }
            else Post(() => Raise(command));
        }
        ApplyPendingBindings();
    }

    internal void Raise(ShortcutCommand command)
    {
        switch (command.Kind)
        {
            case ShortcutCommandKind.TalkStart: Pressed?.Invoke(this, EventArgs.Empty); break;
            case ShortcutCommandKind.TalkEnd: Released?.Invoke(this, new ShortcutEndedEventArgs(command.Cancelled)); break;
            case ShortcutCommandKind.Cancel: Cancelled?.Invoke(this, EventArgs.Empty); break;
            case ShortcutCommandKind.TextMode: TextMode?.Invoke(this, EventArgs.Empty); break;
            case ShortcutCommandKind.DictateStart: DictateStarted?.Invoke(this, EventArgs.Empty); break;
            case ShortcutCommandKind.DictateEnd: DictateEnded?.Invoke(this, new ShortcutEndedEventArgs(command.Cancelled)); break;
            case ShortcutCommandKind.HandsFreeToggle: HandsFreeToggled?.Invoke(this, EventArgs.Empty); break;
        }
    }

    private void Post(Action action)
    {
        if (_context is { } context) context.Post(_ => action(), null);
        else action();
    }

    /// <summary>An unassigned key between Win-down and Win-up stops the shell opening Start. Injected, so the
    /// state machine ignores it.</summary>
    private static void SendStartMenuMask()
    {
        var inputs = new NativeMethods.Input[2];
        inputs[0].Type = inputs[1].Type = 1; // INPUT_KEYBOARD
        inputs[0].U.Ki.WVk = inputs[1].U.Ki.WVk = VkUnassignedMask;
        inputs[1].U.Ki.DwFlags = 0x0002; // KEYEVENTF_KEYUP
        NativeMethods.SendInput(2, inputs, Marshal.SizeOf<NativeMethods.Input>());
    }

    private static IntPtr ResolveModuleHandle()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            if (process.MainModule?.ModuleName is { Length: > 0 } name && NativeMethods.GetModuleHandle(name) is var handle && handle != IntPtr.Zero)
                return handle;
        }
        catch
        {
            // MainModule can throw for single-file / trimmed hosts.
        }
        return NativeMethods.GetModuleHandle(IntPtr.Zero);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _timer.Dispose();
    }

    private static class NativeMethods
    {
        public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct KbdLlHookStruct
        {
            public int VkCode;
            public int ScanCode;
            public int Flags;
            public int Time;
            public IntPtr DwExtraInfo;
        }


        // INPUT is 40 bytes on x64: the union must be sized to MOUSEINPUT or SendInput fails with error 87.
        [StructLayout(LayoutKind.Sequential)]
        public struct Input
        {
            public int Type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MouseInput Mi;
            [FieldOffset(0)] public KeybdInput Ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MouseInput
        {
            public int Dx;
            public int Dy;
            public uint MouseData;
            public uint DwFlags;
            public uint Time;
            public IntPtr DwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KeybdInput
        {
            public ushort WVk;
            public ushort WScan;
            public uint DwFlags;
            public uint Time;
            public IntPtr DwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetModuleHandleW")]
        public static extern IntPtr GetModuleHandle(IntPtr lpModuleName);
    }
}
