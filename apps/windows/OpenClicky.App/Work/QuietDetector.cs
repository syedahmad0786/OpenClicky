using System.Runtime.InteropServices;
using Microsoft.Win32;
using OpenClicky.Core.Work;

namespace OpenClicky.App;

/// <summary>Reads, never writes, what Windows says about the user's focus: the notification state (busy, full screen, presenting, quiet
/// hours) and whether another app is using the microphone (a call). Any thread; a read that fails counts as not quiet.</summary>
internal static class QuietDetector
{
    private const string MicrophoneConsent = @"Software\Microsoft\Windows\CapabilityAccessManager\ConsentStore\microphone";

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    public static QuietState Current() => QuietState.From(NotificationState(), MicInUseByOtherApp(Environment.ProcessPath));

    private static int NotificationState()
    {
        try { return SHQueryUserNotificationState(out var state) >= 0 ? state : 5; }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or COMException) { return 5; }
    }

    /// <summary>True when a packaged app, or a desktop app other than <paramref name="ownExe"/>, has started using the microphone and not stopped.</summary>
    internal static bool MicInUseByOtherApp(string? ownExe)
    {
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(MicrophoneConsent);
            if (root is null) return false;
            foreach (var name in root.GetSubKeyNames())
            {
                using var key = root.OpenSubKey(name);
                if (key is null) continue;
                if (!string.Equals(name, "NonPackaged", StringComparison.OrdinalIgnoreCase)) { if (InUse(key)) return true; continue; }
                var own = ownExe?.Replace('\\', '#');
                foreach (var app in key.GetSubKeyNames())
                {
                    if (own is not null && string.Equals(app, own, StringComparison.OrdinalIgnoreCase)) continue;
                    using var appKey = key.OpenSubKey(app);
                    if (appKey is not null && InUse(appKey)) return true;
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        return false;
    }

    // ponytail: an app that crashed with the mic open leaves start > stop forever; add a staleness cutoff if that shows up.
    /// <summary>Windows stamps a start time when an app opens the microphone and a stop time (0 while open) when it closes it.</summary>
    private static bool InUse(RegistryKey key) =>
        key.GetValue("LastUsedTimeStart") is long start && (key.GetValue("LastUsedTimeStop") is not long stop || start > stop);
}
