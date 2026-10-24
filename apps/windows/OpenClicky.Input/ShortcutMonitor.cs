using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenClicky.Core;
using OpenClicky.Core.Input;
using OpenClicky.Core.Settings;

namespace OpenClicky.Input;

/// <summary>
/// Hosts <see cref="ShortcutStateMachine"/> on a <c>WH_KEYBOARD_LL</c> hook that runs on its own background thread with its
/// own message loop, so a busy UI thread (UI Automation against a slow app) can never stall every key in the system or get
/// the hook removed by <c>LowLevelHooksTimeout</c>. The state machine is only touched on that thread; calls from the UI
/// thread are queued to it. Pressed/Released are talk; Released carries <see cref="ShortcutEndedEventArgs"/>. Events are
/// posted to the UI SynchronizationContext captured in <see cref="Start"/>. Only a binding's non-modifier key (and
/// non-modifier keys while recording) is swallowed; injected input and AltGr's synthetic Ctrl pass through.
/// Mouse clicks arrive through <see cref="NotifyMouseButton"/> (no mouse hook).
/// Unlike the old push-to-talk monitor, <see cref="Cancelled"/> fires on Esc only while an action is active;
/// <see cref="EscapePressed"/> fires on every physical Esc press (never swallowed).
/// Before <see cref="Start"/> and after <see cref="Stop"/> there is no hook thread, and calls run on the caller (tests).
/// </summary>
public sealed class ShortcutMonitor : IPushToTalkMonitor
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100, WmKeyUp = 0x0101, WmSysKeyDown = 0x0104, WmSysKeyUp = 0x0105;
    private const uint WmQuit = 0x0012, WmTimer = 0x0113, WmRunQueued = 0x8001; // WM_APP + 1
    private const int LlkhfInjected = 0x10;
    private const ushort VkUnassignedMask = 0xE8;
    private const int VkEscape = 0x1B, VkLControl = 0xA2;
    /// <summary>The LCtrl that AltGr synthesises (not flagged injected) carries this scan code.</summary>
    private const int AltGrControlScanCode = 0x21D;
    private const uint TickMs = 15;

    private readonly NativeMethods.HookProc _keyboardProc;
    private readonly ShortcutStateMachine _machine;
    private readonly ConcurrentQueue<Action> _queued = new();
    private SynchronizationContext? _context;
    private Action<HotkeyBinding?>? _recordDone;
    private IntPtr _keyboardHook;
    private ShortcutBindings? _pendingBindings;
    private volatile ShortcutBindings _requestedBindings;
    private Thread? _thread;
    private uint _threadId;
    private UIntPtr _timerId;
    private bool _escapeDown;
    private volatile bool _disposed;

    public ShortcutMonitor(ShortcutBindings? bindings = null)
    {
        _requestedBindings = bindings ?? ShortcutBindings.Default;
        _machine = new ShortcutStateMachine(_requestedBindings);
        _keyboardProc = KeyboardCallback; // keep the delegate alive for the native callback
    }

    /// <summary>The latest bindings set. While a shortcut is pending, held or on, the new bindings wait until it ends.</summary>
    public ShortcutBindings Bindings
    {
        get => _requestedBindings;
        set
        {
            _requestedBindings = value;
            OnHookThread(() =>
            {
                _pendingBindings = value;
                ApplyPendingBindings();
            });
        }
    }

    private void ApplyPendingBindings()
    {
        if (_pendingBindings is not { } bindings || !_machine.Idle) return;
        _pendingBindings = null;
        _machine.Bindings = bindings;
        UpdateTimer();
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

    /// <summary>Call on the UI thread: its SynchronizationContext receives every event. Throws when the hook cannot be installed.</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_thread is not null) return;
        _context = SynchronizationContext.Current;
        var started = new StartResult();
        var thread = new Thread(HookThread) { IsBackground = true, Name = "OpenClicky shortcut hook" };
        _thread = thread;
        thread.Start(started);
        started.Ready.Wait();
        started.Ready.Dispose();
        if (started.Error is not { } error) return;
        thread.Join();
        _thread = null;
        throw new InvalidOperationException($"SetWindowsHookEx failed: {error}");
    }

    /// <summary>Unhooks and ends any active action as cancelled (and any recording with null).</summary>
    public void Stop()
    {
        if (_thread is not { } thread)
        {
            Run(_machine.Reset());
            return;
        }
        _thread = null; // from here on calls run on the caller; the hook thread resets the machine as it exits
        NativeMethods.PostThreadMessage(_threadId, WmQuit, UIntPtr.Zero, IntPtr.Zero);
        if (thread.ManagedThreadId != Environment.CurrentManagedThreadId && !thread.Join(TimeSpan.FromSeconds(2)))
            Trace.TraceWarning("ShortcutMonitor: the hook thread did not stop within 2 s.");
    }

    /// <summary>
    /// Captures the next chord for <paramref name="action"/> (hold for talk/dictate, double-tap for text/hands-free).
    /// <paramref name="done"/> gets the binding, or null on Esc, 7 s timeout, Stop or a newer Record call.
    /// </summary>
    public void Record(ShortcutAction action, Action<HotkeyBinding?> done)
    {
        var now = Environment.TickCount64;
        OnHookThread(() =>
        {
            var previous = _recordDone;
            _recordDone = done;
            if (previous is not null) Post(() => previous(null));
            _machine.BeginRecording(action, now);
            UpdateTimer();
        });
    }

    private sealed class StartResult
    {
        public readonly ManualResetEventSlim Ready = new();
        public int? Error;
    }

    private void HookThread(object? state)
    {
        var started = (StartResult)state!;
        NativeMethods.PeekMessage(out _, IntPtr.Zero, 0, 0, 0); // PM_NOREMOVE: creates this thread's queue before anyone posts to it
        _threadId = NativeMethods.GetCurrentThreadId();
        _keyboardHook = NativeMethods.SetWindowsHookEx(WhKeyboardLl, _keyboardProc, ResolveModuleHandle(), 0);
        if (_keyboardHook == IntPtr.Zero)
        {
            started.Error = Marshal.GetLastWin32Error();
            started.Ready.Set();
            return;
        }
        started.Ready.Set();
        try
        {
            while (NativeMethods.GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                if (message.Message == WmTimer && message.HWnd == IntPtr.Zero) Guarded(Tick);
                else if (message.Message == WmRunQueued) DrainQueue();
                else NativeMethods.DispatchMessage(ref message);
            }
        }
        finally
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
            DrainQueue();
            Guarded(() => Run(_machine.Reset()));
            StopTimer();
        }
    }

    private static void Guarded(Action action)
    {
        try { action(); }
        catch (Exception ex) { Trace.TraceError($"ShortcutMonitor: {ex}"); } // the hook thread must keep running
    }

    private void DrainQueue()
    {
        while (_queued.TryDequeue(out var action)) Guarded(action);
    }

    private bool OnHookThreadNow => _thread is { } thread && thread.ManagedThreadId == Environment.CurrentManagedThreadId;

    /// <summary>Runs <paramref name="action"/> on the hook thread (inline when there is none, or when already on it).</summary>
    private void OnHookThread(Action action)
    {
        if (_thread is null || OnHookThreadNow) { action(); return; }
        _queued.Enqueue(action);
        if (!NativeMethods.PostThreadMessage(_threadId, WmRunQueued, UIntPtr.Zero, IntPtr.Zero)) DrainQueue(); // the thread is gone
    }

    /// <summary>Hook thread: a 15 ms thread timer runs while the machine needs ticks. Without a hook thread nothing ticks.</summary>
    private void UpdateTimer()
    {
        if (!OnHookThreadNow) return;
        if (_machine.NeedsTick && _timerId == UIntPtr.Zero) _timerId = NativeMethods.SetTimer(IntPtr.Zero, UIntPtr.Zero, TickMs, IntPtr.Zero);
        else if (!_machine.NeedsTick) StopTimer();
    }

    private void StopTimer()
    {
        if (_timerId == UIntPtr.Zero) return;
        NativeMethods.KillTimer(IntPtr.Zero, _timerId);
        _timerId = UIntPtr.Zero;
    }

    private void Tick()
    {
        var now = Environment.TickCount64;
        DropMissedKeyUps(currentVk: -1, now); // a running hold must not outlive a key-up the hook never saw
        Run(_machine.Tick(now));
        UpdateTimer();
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
                if (HandleKey(data.VkCode, down, (data.Flags & LlkhfInjected) != 0, Environment.TickCount64, data.ScanCode)) return 1;
            }
        }
        catch (Exception ex)
        {
            Trace.TraceError($"ShortcutMonitor keyboard hook: {ex}"); // never break the user's keyboard
        }
        return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    /// <summary>One keyboard event from the hook (hook thread); returns true when it must be swallowed.</summary>
    internal bool HandleKey(int vk, bool down, bool injected, long now, int scanCode = 0)
    {
        if (vk == VkLControl && scanCode == AltGrControlScanCode) return false; // AltGr is not Ctrl+Alt
        if (!injected) DropMissedKeyUps(vk, now);
        if (_escapeDown && vk != VkEscape && !IsKeyDown(VkEscape)) _escapeDown = false; // its key-up was missed
        if (!injected && vk == VkEscape)
        {
            if (down && !_escapeDown && !_machine.IsRecording) Post(() => EscapePressed?.Invoke(this, EventArgs.Empty));
            _escapeDown = down;
        }
        var commands = _machine.OnKey(vk, down, injected, now);
        var swallow = _machine.SwallowLastKey;
        Run(commands); // MaskStartMenu is sent here, before this Win key-up is passed on
        UpdateTimer();
        return swallow;
    }

    /// <summary>
    /// A physical (not injected) mouse button went down, e.g. from the raw-input PhysicalUserActivityMonitor (an input
    /// sink that never blocks input). It poisons shortcuts that have not fired yet (Ctrl+click is not a Ctrl tap); a
    /// running hold keeps going. Any thread: queued to the hook thread.
    /// </summary>
    public void NotifyMouseButton()
    {
        var now = Environment.TickCount64;
        OnHookThread(() =>
        {
            Run(_machine.OnMouseButton(now));
            UpdateTimer();
        });
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

    /// <summary>Nothing is raised once disposed: at shutdown the UI's marshaling window may already be gone.</summary>
    private void Post(Action action)
    {
        if (_disposed) return;
        if (_context is not { } context) { action(); return; }
        try { context.Post(_ => { if (!_disposed) action(); }, null); }
        catch (InvalidOperationException ex) { Trace.TraceWarning($"ShortcutMonitor: event dropped, the UI is gone ({ex.GetType().Name})."); }
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

        [StructLayout(LayoutKind.Sequential)]
        public struct Msg
        {
            public IntPtr HWnd;
            public uint Message;
            public UIntPtr WParam;
            public IntPtr LParam;
            public uint Time;
            public int PtX;
            public int PtY;
            public uint Private;
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
        public static extern int GetMessage(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PeekMessage(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

        [DllImport("user32.dll")]
        public static extern IntPtr DispatchMessage(ref Msg lpMsg);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostThreadMessage(uint idThread, uint msg, UIntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern UIntPtr SetTimer(IntPtr hWnd, UIntPtr nIDEvent, uint uElapse, IntPtr lpTimerFunc);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool KillTimer(IntPtr hWnd, UIntPtr uIDEvent);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

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
