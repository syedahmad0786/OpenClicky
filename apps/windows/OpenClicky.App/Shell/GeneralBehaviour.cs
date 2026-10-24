using System.Runtime.InteropServices;
using Microsoft.Win32;
using OpenClicky.Core.Shell;
using static OpenClicky.App.Surfaces.ShellSurfaceNative;

namespace OpenClicky.App.Shell;

/// <summary>Windows side of the shell's General settings plus the theme/motion the shell follows.</summary>
internal static class GeneralBehaviour
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "OpenClicky";
    private const uint SpiGetClientAreaAnimation = 0x1042;

    /// <summary>Writes or deletes HKCU\...\Run\OpenClicky = "&lt;exe&gt;" --background.</summary>
    public static void SetLaunchAtLogin(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(RunValue, $"\"{Application.ExecutablePath}\" --background");
        else key.DeleteValue(RunValue, throwOnMissingValue: false);
    }

    /// <summary>Excludes a window of this process from screen capture (or includes it again). False when Windows refused.</summary>
    public static bool ApplyCaptureExclusion(nint hwnd, bool exclude) =>
        hwnd != 0 && SetWindowDisplayAffinity(hwnd, exclude ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE);

    /// <summary>AppsUseLightTheme = 0 means dark; missing means the Windows default (light).</summary>
    public static ShellTheme ReadTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int light && light == 0 ? ShellTheme.Dark : ShellTheme.Light;
    }

    public static bool ReducedMotion()
    {
        if (!SystemInformation.UIEffectsEnabled) return true;
        return SystemParametersInfo(SpiGetClientAreaAnimation, 0, out var animate, 0) && !animate;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, [MarshalAs(UnmanagedType.Bool)] out bool value, uint winIni);
}

/// <summary>
/// The taskbar button for "Show in taskbar": a borderless, fully transparent 1×1 window parked off-screen.
/// Activating it (clicking the button) opens the notch.
/// </summary>
internal sealed class TaskbarPresence : Form
{
    private readonly Action _activated;

    public TaskbarPresence(Action activated)
    {
        _activated = activated;
        Text = "OpenClicky";
        Icon = AppTheme.Icon;
        ShowInTaskbar = true;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(-32000, -32000, 1, 1);
        Opacity = 0;
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        _activated();
    }
}
