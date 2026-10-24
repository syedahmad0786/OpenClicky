using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenClicky.Core;
using OpenClicky.Core.Input;
using OpenClicky.Core.Settings;

namespace OpenClicky.Input;

public sealed class ShortcutEndedEventArgs(bool cancelled) : EventArgs
{
    /// <summary>A tap, another key, Esc or Stop ended it: discard rather than send.</summary>
    public bool Cancelled { get; } = cancelled;
}

/// <summary>
/// Hosts <see cref="ShortcutStateMachine"/> on a <c>WH_KEYBOARD_LL</c> hook (UI thread). Pressed/Released are talk;
/// Released carries <see cref="ShortcutEndedEventArgs"/>. Events are posted to the UI SynchronizationContext captured
/// in <see cref="Start"/> so handlers never run inside (and stall) the hook. Only the non-modifier key of an active
/// hold binding (and non-modifier keys while recording) is swallowed; injected input passes through untouched.
/// Change from <see cref="PushToTalkHotkeyMonitor"/>: <see cref="Cancelled"/> fires on Esc only while an action is active.
/// </summary>
public sealed class ShortcutMonitor : IPushToTalkMonitor
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100, WmKeyUp = 0x0101, WmSysKeyDown = 0x0104, WmSysKeyUp = 0x0105;
    private const int LlkhfInjected = 0x10;
    private const ushort VkUnassignedMask = 0xE8;

    private readonly NativeMethods.LowLevelKeyboardProc _hookProc;
    private readonly ShortcutStateMachine _machine;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };
    private SynchronizationContext? _context;
    private Action<HotkeyBinding?>? _recordDone;
    private IntPtr _hookId;
    private bool _disposed;

    public ShortcutMonitor(ShortcutBindings? bindings = null)
    {
        _machine = new ShortcutStateMachine(bindings ?? ShortcutBindings.Default);
        _hookProc = HookCallback; // keep the delegate alive for the native callback
        _timer.Tick += (_, _) =>
        {
            Run(_machine.Tick(Environment.TickCount64));
            if (!_machine.NeedsTick) _timer.Stop();
        };
    }

    /// <summary>Set while no shortcut is held (it resets per-binding state).</summary>
    public ShortcutBindings Bindings { get => _machine.Bindings; set => _machine.Bindings = value; }

    /// <summary>Ignored: shortcuts come from <see cref="Bindings"/>. Kept for <see cref="IPushToTalkMonitor"/>.</summary>
    public HotkeyChord Chord { get; set; } = HotkeyChord.Default;

    public event EventHandler? Pressed;
    public event EventHandler? Released;
    public event EventHandler? Cancelled;
    public event EventHandler? TextMode;
    public event EventHandler? DictateStarted;
    public event EventHandler<ShortcutEndedEventArgs>? DictateEnded;
    public event EventHandler? HandsFreeToggled;

    public void Start()
    {
        if (_hookId != IntPtr.Zero) return;
        _context = SynchronizationContext.Current;
        _hookId = NativeMethods.SetWindowsHookEx(WhKeyboardLl, _hookProc, ResolveModuleHandle(), 0);
        if (_hookId == IntPtr.Zero) throw new InvalidOperationException($"SetWindowsHookEx failed: {Marshal.GetLastWin32Error()}");
    }

    /// <summary>Unhooks and ends any active action as cancelled (and any recording with null).</summary>
    public void Stop()
    {
        if (_hookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
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

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        var message = wParam.ToInt32();
        var down = message is WmKeyDown or WmSysKeyDown;
        if (nCode >= 0 && (down || message is WmKeyUp or WmSysKeyUp))
        {
            var data = Marshal.PtrToStructure<NativeMethods.KbdLlHookStruct>(lParam);
            var injected = (data.Flags & LlkhfInjected) != 0;
            var now = Environment.TickCount64;
            if (!injected) DropMissedKeyUps(data.VkCode, now);
            var commands = _machine.OnKey(data.VkCode, down, injected, now);
            var swallow = _machine.SwallowLastKey;
            Run(commands); // MaskStartMenu is sent here, before this Win key-up is passed on
            if (_machine.NeedsTick) _timer.Start();
            if (swallow) return 1;
        }
        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    /// <summary>The hook misses key-ups that happen on the secure desktop (Win+L, Ctrl+Alt+Del); release any key
    /// the OS no longer reports down so the session can end.</summary>
    private void DropMissedKeyUps(int currentVk, long now)
    {
        foreach (var vk in _machine.PressedPassedKeys)
            if (vk != currentVk && (NativeMethods.GetAsyncKeyState(vk) & 0x8000) == 0)
                Run(_machine.OnKey(vk, down: false, injected: false, now));
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
        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

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
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

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
