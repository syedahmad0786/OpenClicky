using System.Runtime.InteropServices;

namespace OpenClicky.Agent;

/// <summary>
/// The mandatory integrity level (RID: Low 0x1000, Medium 0x2000, High 0x3000, System 0x4000) of a window's process, read with
/// query-only rights. Used to refuse input Windows would silently drop (UIPI); it never elevates or works around it.
/// </summary>
public static class ProcessIntegrity
{
    private const uint ProcessQueryLimitedInformation = 0x1000, TokenQuery = 0x0008;
    private const int TokenIntegrityLevel = 25;
    private static readonly Lazy<int> OwnRid = new(() => OfProcess(GetCurrentProcess(), ownsHandle: false) ?? 0x2000);

    /// <summary>The integrity of the process that owns <paramref name="hwnd"/>; null when the window, process or token can't be read.</summary>
    public static int? Of(nint hwnd)
    {
        if (hwnd == 0 || GetWindowThreadProcessId(hwnd, out var pid) == 0 || pid == 0) return null;
        var process = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        return process == 0 ? null : OfProcess(process, ownsHandle: true);
    }

    /// <summary>OpenClicky's own integrity (Medium when it somehow cannot be read).</summary>
    public static int Own() => OwnRid.Value;

    private static int? OfProcess(nint process, bool ownsHandle)
    {
        nint token = 0, buffer = 0;
        try
        {
            if (!OpenProcessToken(process, TokenQuery, out token)) return null;
            _ = GetTokenInformation(token, TokenIntegrityLevel, 0, 0, out var size);
            if (size == 0) return null;
            buffer = Marshal.AllocHGlobal((int)size);
            if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, size, out _)) return null;
            // TOKEN_MANDATORY_LABEL { SID_AND_ATTRIBUTES { PSID Sid; DWORD Attributes } }: the RID is the SID's last sub-authority.
            var sid = Marshal.ReadIntPtr(buffer);
            if (!IsValidSid(sid)) return null;
            var count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
            return count == 0 ? null : Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1)));
        }
        finally
        {
            if (buffer != 0) Marshal.FreeHGlobal(buffer);
            if (token != 0) CloseHandle(token);
            if (ownsHandle) CloseHandle(process);
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, bool inherit, uint processId);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(nint process, uint access, out nint token);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(nint token, int infoClass, nint info, uint length, out uint returned);

    [DllImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsValidSid(nint sid);

    [DllImport("advapi32.dll")]
    private static extern nint GetSidSubAuthorityCount(nint sid);

    [DllImport("advapi32.dll")]
    private static extern nint GetSidSubAuthority(nint sid, uint index);
}
