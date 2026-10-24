using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenClicky.Core;

namespace OpenClicky.Input;

/// <summary>
/// Hold-to-talk via a low-level keyboard hook so modifier+key chords raise Pressed/Released.
/// Swallows the chord's main key while held so Space (etc.) does not type into the focused app.
/// </summary>
public sealed class PushToTalkHotkeyMonitor : IPushToTalkMonitor
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;

    private readonly NativeMethods.LowLevelKeyboardProc _hookProc;
    private IntPtr _hookId = IntPtr.Zero;
    private bool _isChordDown;
    private bool _disposed;
    private readonly HashSet<int> _pressedKeys = new();
    private bool _cancelledUntilRelease;

    public PushToTalkHotkeyMonitor(HotkeyChord? chord = null)
    {
        Chord = chord ?? HotkeyChord.Default;
        // Keep delegate alive for the native callback.
        _hookProc = HookCallback;
    }

    public HotkeyChord Chord { get; set; }

    public event EventHandler? Pressed;

    public event EventHandler? Released;

    public event EventHandler? Cancelled;

    public void Start()
    {
        if (_hookId != IntPtr.Zero)
        {
            return;
        }

        // Single-file publish often has a null/unusable MainModule — GetModuleHandle(NULL) is fine for LL hooks.
        var moduleHandle = ResolveModuleHandle();
        _hookId = NativeMethods.SetWindowsHookEx(
            WhKeyboardLl,
            _hookProc,
            moduleHandle,
            0);

        if (_hookId == IntPtr.Zero)
        {
            throw new InvalidOperationException($"SetWindowsHookEx failed: {Marshal.GetLastWin32Error()}");
        }
    }

    private static IntPtr ResolveModuleHandle()
    {
        try
        {
            using var curProcess = Process.GetCurrentProcess();
            var module = curProcess.MainModule;
            if (module?.ModuleName is { Length: > 0 } name)
            {
                var handle = NativeMethods.GetModuleHandle(name);
                if (handle != IntPtr.Zero)
                {
                    return handle;
                }
            }
        }
        catch
        {
            // MainModule can throw for single-file / trimmed hosts.
        }

        return NativeMethods.GetModuleHandle(IntPtr.Zero);
    }

    public void Stop()
    {
        if (_hookId == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.UnhookWindowsHookEx(_hookId);
        _hookId = IntPtr.Zero;
        _pressedKeys.Clear();
        _cancelledUntilRelease = false;
        if (_isChordDown)
        {
            _isChordDown = false;
            Released?.Invoke(this, EventArgs.Empty);
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var message = wParam.ToInt32();
            var data = Marshal.PtrToStructure<NativeMethods.KbdLlHookStruct>(lParam);
            var vk = data.VkCode;
            var isDown = message is WmKeyDown or WmSysKeyDown;
            var isUp = message is WmKeyUp or WmSysKeyUp;

            // Injected agent input must not retrigger voice or cancel its own task.
            if ((data.Flags & 0x10) != 0) return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
            if (isDown) _pressedKeys.Add(vk);
            if (isUp) _pressedKeys.Remove(vk);
            if (isDown && vk == 0x1B)
            {
                _cancelledUntilRelease = _isChordDown;
                _isChordDown = false;
                Cancelled?.Invoke(this, EventArgs.Empty);
            }
            if (!_pressedKeys.Contains(Chord.VirtualKey)) _cancelledUntilRelease = false;

            var wasDown = _isChordDown;
            if (isDown || isUp)
            {
                EvaluateChordState();
            }

            // Suppress the chord's primary key while PTT is active (and its matching key-up)
            // so e.g. Win+Alt+Space does not insert spaces into the focused window.
            if (vk == Chord.VirtualKey && (_isChordDown || wasDown))
            {
                return (IntPtr)1;
            }
        }

        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private void EvaluateChordState()
    {
        // LL callbacks run before GetAsyncKeyState updates; track the event itself.
        var matches = !_cancelledUntilRelease && Chord.MatchesPressedKeys(_pressedKeys);

        if (matches && !_isChordDown)
        {
            _isChordDown = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }
        else if (!matches && _isChordDown)
        {
            _isChordDown = false;
            Released?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
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

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetModuleHandleW")]
        public static extern IntPtr GetModuleHandle(IntPtr lpModuleName);

    }
}
