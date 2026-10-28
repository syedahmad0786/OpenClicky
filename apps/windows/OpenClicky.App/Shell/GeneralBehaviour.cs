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

    /// <summary>
    /// The signed-in account's full name (GetUserNameExW NameDisplay); empty for local accounts without one, and
    /// until <see cref="WarmAccountDisplayName"/> has read it. Never blocks.
    /// </summary>
    public static string AccountDisplayName => _accountName ?? "";
    private static volatile string? _accountName;

    /// <summary>Reads the account name once on a background thread (it can block on a domain controller), then calls <paramref name="ready"/>.</summary>
    public static void WarmAccountDisplayName(Action ready) => Task.Run(() =>
    {
        _accountName = ReadAccountDisplayName();
        ready();
    });

    private static string ReadAccountDisplayName()
    {
        const int NameDisplay = 3, ErrorMoreData = 234;
        try
        {
            uint size = 256;
            var buffer = new System.Text.StringBuilder((int)size);
            if (GetUserNameExW(NameDisplay, buffer, ref size)) return buffer.ToString();
            if (Marshal.GetLastWin32Error() != ErrorMoreData || size is 0 or > 32_768) return "";
            buffer = new System.Text.StringBuilder((int)size); // size now includes the terminator
            return GetUserNameExW(NameDisplay, buffer, ref size) ? buffer.ToString() : "";
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or ArgumentException) { return ""; }
    }

    [DllImport("secur32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetUserNameExW(int nameFormat, System.Text.StringBuilder name, ref uint size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, [MarshalAs(UnmanagedType.Bool)] out bool value, uint winIni);
}

/// <summary>
/// The taskbar button for "Show in taskbar": a borderless, fully transparent 1×1 window parked off-screen.
/// Activating it (clicking the button) opens the notch, except when activation comes back from the shell surface itself
/// (the page releasing keyboard focus restores the previous foreground, which can be this window); that would reopen
/// the notch in a loop.
/// </summary>
internal sealed class TaskbarPresence : Form
{
    private const int WmActivate = 0x0006;
    private readonly Action _activated;
    private readonly Func<nint, bool> _isShellWindow;

    public TaskbarPresence(Action activated, Func<nint, bool> isShellWindow)
    {
        _activated = activated;
        _isShellWindow = isShellWindow;
        Text = "OpenClicky";
        Icon = AppTheme.Icon;
        ShowInTaskbar = true;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(-32000, -32000, 1, 1);
        Opacity = 0;
    }

    /// <summary>Appearing must not count as a click on the taskbar button.</summary>
    protected override bool ShowWithoutActivation => true;

    protected override void WndProc(ref Message m)
    {
        // WM_ACTIVATE: low word of wParam is WA_INACTIVE (0) or active; lParam is the window losing activation.
        if (m.Msg == WmActivate && (m.WParam.ToInt64() & 0xFFFF) != 0 && !_isShellWindow(m.LParam)) _activated();
        base.WndProc(ref m);
    }
}
