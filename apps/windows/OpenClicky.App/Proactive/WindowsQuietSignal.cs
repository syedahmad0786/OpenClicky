using System.Runtime.InteropServices;
using Microsoft.Win32;
using OpenClicky.Agent;
using OpenClicky.Core.Proactive;

namespace OpenClicky.App.Proactive;

/// <summary>
/// Quiet while Windows says a full-screen app, a presentation or quiet hours is on, or while another app is recording the
/// microphone (a call). Reasons are fixed strings, never an app name, path or title. Read fresh each call; nothing is cached.
/// </summary>
// ponytail: stand-in heuristic; sub-project E's QuietState replaces this at merge.
internal sealed class WindowsQuietSignal(Action<string> log) : IQuietSignal
{
    private const string MicrophoneKey = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";
    private bool _notificationErrorLogged, _microphoneErrorLogged;

    public string? QuietReason() => NotificationReason() ?? (MicrophoneInUse() ? "On a call" : null);

    private string? NotificationReason()
    {
        try
        {
            if (SHQueryUserNotificationState(out var state) != 0) return null;
            return state switch
            {
                // The notch surface covers its monitor while it takes input: our own window is never "full screen" (as in ForegroundFullscreen).
                2 or 3 => ForegroundWindowInfo.IsCurrentProcessForeground() ? null : "Full screen",
                4 => "Presenting",
                6 => "Quiet hours",
                _ => null,
            };
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogOnce(ref _notificationErrorLogged, ex);
            return null;
        }
    }

    private bool MicrophoneInUse()
    {
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(MicrophoneKey);
            if (root is null) return false;
            using var nonPackaged = root.OpenSubKey("NonPackaged");
            return AnyInUse(root) || (nonPackaged is not null && AnyInUse(nonPackaged));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogOnce(ref _microphoneErrorLogged, ex);
            return false;
        }
    }

    private static bool AnyInUse(RegistryKey parent)
    {
        var own = Environment.ProcessPath;
        foreach (var name in parent.GetSubKeyNames())
        {
            using var app = parent.OpenSubKey(name);
            if (app is not null && ProactiveHostInputs.MicInUseByOther(name, app.GetValue("LastUsedTimeStart") as long?, app.GetValue("LastUsedTimeStop") as long?, own))
                return true;
        }
        return false;
    }

    private void LogOnce(ref bool logged, Exception ex)
    {
        if (logged) return;
        logged = true;
        log("proactive_quiet_error=" + ex.GetType().Name);
    }

    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);
}
