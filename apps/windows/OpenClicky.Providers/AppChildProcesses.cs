using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OpenClicky.Providers;

/// <summary>
/// One Windows Job Object per app, closed only when the process ends: every runtime server put in it (and everything those start,
/// which inherits the job) is killed with the app, even on a crash. Best effort: if Windows refuses, the server still runs and is
/// stopped by its own dispose as before.
/// </summary>
internal static class AppChildProcesses
{
    private const int ExtendedLimitInformation = 9;
    private const uint KillOnJobClose = 0x2000;
    private static readonly Lazy<nint> Job = new(Create);

    /// <summary>False when the process could not be placed in the job.</summary>
    public static bool Adopt(Process process)
    {
        var job = Job.Value;
        try { return job != 0 && AssignProcessToJobObject(job, process.Handle); }
        catch (InvalidOperationException) { return false; } // already exited
    }

    private static nint Create()
    {
        var job = CreateJobObject(0, null);
        if (job == 0) return 0;
        var info = new ExtendedLimit { Basic = new BasicLimit { LimitFlags = KillOnJobClose } };
        if (SetInformationJobObject(job, ExtendedLimitInformation, ref info, (uint)Marshal.SizeOf<ExtendedLimit>())) return job;
        CloseHandle(job);
        return 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimit
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimit
    {
        public BasicLimit Basic;
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
        public nuint ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern nint CreateJobObject(nint attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(nint job, int infoClass, ref ExtendedLimit info, uint length);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(nint job, nint process);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
}
