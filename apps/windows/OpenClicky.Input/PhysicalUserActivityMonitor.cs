using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace OpenClicky.Input;

public enum PhysicalUserActivityCategory
{
    Mouse,
    Keyboard,
}

/// <param name="MouseButtonDown">A mouse button went down (left, right, middle, X1 or X2).</param>
public sealed record PhysicalUserActivity(PhysicalUserActivityCategory Category, DateTimeOffset TimestampUtc, bool MayChangeScreen = false,
    bool MouseButtonDown = false);

/// <summary>Reports hardware keyboard or mouse activity through WM_INPUT without recording input content.</summary>
public sealed class PhysicalUserActivityMonitor : IDisposable
{
    private readonly RawInputWindow _window;
    private bool _registered;
    private bool _disposed;

    public PhysicalUserActivityMonitor() => _window = new RawInputWindow(OnRawInput);

    public event Action<PhysicalUserActivity>? UserActivity;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_registered) return;
        _window.Create();
        var devices = Devices(InputNativeMethods.RidevInputSink, _window.Handle);
        if (!InputNativeMethods.RegisterRawInputDevices(devices, (uint)devices.Length,
                (uint)Marshal.SizeOf<InputNativeMethods.RawInputDevice>()))
        {
            var error = Marshal.GetLastWin32Error();
            _window.Destroy();
            throw new Win32Exception(error, "Could not monitor physical keyboard and mouse activity.");
        }
        _registered = true;
    }

    public void Stop()
    {
        if (!_registered)
        {
            _window.Destroy();
            return;
        }
        var devices = Devices(InputNativeMethods.RidevRemove, IntPtr.Zero);
        var removed = InputNativeMethods.RegisterRawInputDevices(devices, (uint)devices.Length,
            (uint)Marshal.SizeOf<InputNativeMethods.RawInputDevice>());
        var error = removed ? 0 : Marshal.GetLastWin32Error();
        _registered = false;
        _window.Destroy();
        if (!removed) throw new Win32Exception(error, "Could not stop the physical input monitor.");
    }

    private void OnRawInput(IntPtr rawInput)
    {
        var headerSize = Marshal.SizeOf<RawInputHeader>();
        var bytes = new byte[headerSize + 32];
        uint size = (uint)bytes.Length;
        var read = InputNativeMethods.GetRawInputData(rawInput, InputNativeMethods.RidInput, bytes,
            ref size, (uint)headerSize);
        if (read == uint.MaxValue || read < headerSize || size < headerSize
            || !RawInputClassifier.TryClassifyHeader(bytes, out var category)) return;
        var input = bytes.AsSpan(0, (int)read);
        UserActivity?.Invoke(new(category, DateTimeOffset.UtcNow, RawInputClassifier.MayChangeScreen(input, category),
            RawInputClassifier.MouseButtonDown(input, category)));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { Stop(); }
        catch (Win32Exception) { _window.Destroy(); }
    }

    private static InputNativeMethods.RawInputDevice[] Devices(uint flags, IntPtr target) =>
    [
        new() { UsagePage = 0x01, Usage = 0x02, Flags = flags, Target = target },
        new() { UsagePage = 0x01, Usage = 0x06, Flags = flags, Target = target },
    ];

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader
    {
        internal uint Type;
        internal uint Size;
        internal IntPtr Device;
        internal IntPtr Parameter;
    }

    private sealed class RawInputWindow(Action<IntPtr> received) : NativeWindow
    {
        internal void Create()
        {
            if (Handle != IntPtr.Zero) return;
            CreateHandle(new CreateParams { Caption = "OpenClicky physical input monitor", Parent = new IntPtr(-3) });
        }

        internal void Destroy()
        {
            if (Handle != IntPtr.Zero) DestroyHandle();
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == InputNativeMethods.WmInput) received(message.LParam);
            base.WndProc(ref message);
        }
    }
}

internal static class RawInputClassifier
{
    internal static bool MayChangeScreen(ReadOnlySpan<byte> input, PhysicalUserActivityCategory category)
    {
        var offset = 8 + IntPtr.Size * 2;
        if (input.Length < offset + 6) return false;
        // Only button/wheel flags, key-down state and whether the key is a modifier are inspected; nothing is retained.
        if (category == PhysicalUserActivityCategory.Mouse) return BinaryPrimitives.ReadUInt16LittleEndian(input[(offset + 4)..]) != 0;
        if ((BinaryPrimitives.ReadUInt16LittleEndian(input[(offset + 2)..]) & 1) != 0) return false; // RI_KEY_BREAK
        // A bare Ctrl/Alt/Shift/Win down (a talk press, a text-mode double-tap, their auto-repeat) changes nothing on screen.
        return input.Length < offset + 8 || !IsModifier(BinaryPrimitives.ReadUInt16LittleEndian(input[(offset + 6)..]));
    }

    /// <summary>VK_SHIFT/CONTROL/MENU, VK_LWIN/RWIN and VK_LSHIFT..VK_RMENU.</summary>
    private static bool IsModifier(ushort vk) => vk is >= 0x10 and <= 0x12 or 0x5B or 0x5C or >= 0xA0 and <= 0xA5;

    /// <summary>RI_MOUSE_{LEFT,RIGHT,MIDDLE}_BUTTON_DOWN and RI_MOUSE_BUTTON_{4,5}_DOWN in usButtonFlags.</summary>
    internal static bool MouseButtonDown(ReadOnlySpan<byte> input, PhysicalUserActivityCategory category)
    {
        var offset = 8 + IntPtr.Size * 2;
        return category == PhysicalUserActivityCategory.Mouse && input.Length >= offset + 6
            && (BinaryPrimitives.ReadUInt16LittleEndian(input[(offset + 4)..]) & 0x155) != 0;
    }

    internal static bool TryClassifyHeader(
        ReadOnlySpan<byte> header,
        out PhysicalUserActivityCategory category)
    {
        category = default;
        var headerSize = 8 + IntPtr.Size * 2;
        if (header.Length < headerSize) return false;
        var declaredSize = BinaryPrimitives.ReadUInt32LittleEndian(header[4..8]);
        if (declaredSize < headerSize) return false;
        var device = header.Slice(8, IntPtr.Size);
        if (device.IndexOfAnyExcept((byte)0) < 0) return false;
        switch (BinaryPrimitives.ReadUInt32LittleEndian(header[..4]))
        {
            case InputNativeMethods.RimTypeMouse:
                category = PhysicalUserActivityCategory.Mouse;
                return true;
            case InputNativeMethods.RimTypeKeyboard:
                category = PhysicalUserActivityCategory.Keyboard;
                return true;
            default:
                return false;
        }
    }
}
