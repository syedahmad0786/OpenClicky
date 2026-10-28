using System.Runtime.InteropServices;

namespace OpenClicky.App;

/// <summary>
/// Keeps the PC from sleeping on idle (a process-scoped system-required power request) while <see cref="Set"/> is on.
/// Windows drops the request when the process ends; <see cref="Dispose"/> clears it sooner. It changes no power setting.
/// UI thread only.
/// </summary>
internal sealed class PowerKeepAwake : IDisposable
{
    private const uint PowerRequestContextVersion = 0;
    private const uint PowerRequestContextSimpleString = 0x1;
    private const int PowerRequestSystemRequired = 1;
    private static readonly IntPtr InvalidHandle = new(-1);

    private IntPtr _request;

    public bool IsOn => _request != IntPtr.Zero;

    /// <summary>Holds or clears the request. Returns false when Windows refused it (the PC may then sleep as usual).</summary>
    public bool Set(bool on)
    {
        if (on == IsOn) return true;
        if (!on)
        {
            PowerClearRequest(_request, PowerRequestSystemRequired);
            CloseHandle(_request);
            _request = IntPtr.Zero;
            return true;
        }
        var reason = new ReasonContext
        {
            Version = PowerRequestContextVersion,
            Flags = PowerRequestContextSimpleString,
            SimpleReasonString = "OpenClicky has routines scheduled",
        };
        var request = PowerCreateRequest(ref reason);
        if (request == IntPtr.Zero || request == InvalidHandle) return false;
        if (!PowerSetRequest(request, PowerRequestSystemRequired)) { CloseHandle(request); return false; }
        _request = request;
        return true;
    }

    public void Dispose() => Set(false);

    // Only the simple-string arm of the native union is used, so the struct stops at its pointer.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ReasonContext
    {
        public uint Version;
        public uint Flags;
        [MarshalAs(UnmanagedType.LPWStr)] public string SimpleReasonString;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr PowerCreateRequest(ref ReasonContext context);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PowerSetRequest(IntPtr request, int requestType);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PowerClearRequest(IntPtr request, int requestType);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
