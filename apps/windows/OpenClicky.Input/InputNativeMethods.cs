using System.Runtime.InteropServices;

namespace OpenClicky.Input;

internal static class InputNativeMethods
{
    internal const int WmInput = 0x00FF;
    internal const uint RidHeader = 0x10000005;
    internal const uint RidInput = 0x10000003;
    internal const uint RidevRemove = 0x00000001;
    internal const uint RidevInputSink = 0x00000100;
    internal const uint RimTypeMouse = 0;
    internal const uint RimTypeKeyboard = 1;

    [StructLayout(LayoutKind.Sequential)]
    internal struct RawInputDevice
    {
        internal ushort UsagePage;
        internal ushort Usage;
        internal uint Flags;
        internal IntPtr Target;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterRawInputDevices(
        RawInputDevice[] devices,
        uint deviceCount,
        uint deviceSize);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetRawInputData(
        IntPtr rawInput,
        uint command,
        byte[] data,
        ref uint size,
        uint headerSize);
}
