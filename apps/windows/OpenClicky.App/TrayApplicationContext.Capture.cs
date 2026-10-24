using System.Runtime.InteropServices;
using OpenClicky.App.Capture;
using OpenClicky.Core;

namespace OpenClicky.App;

internal sealed partial class TrayApplicationContext
{
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);

    /// <summary>Talk screenshots cover every display without OpenClicky's own windows (the agent keeps the plain capture).</summary>
    private IScreenCaptureService CreateTalkCapture() =>
        new OwnWindowsExcludedCapture(_capture, OwnWindowsForCapture, _overlay.Hide, Log);

    /// <summary>
    /// The windows of this process that are on screen right now: shell surface, buddy overlay and its tip, dock,
    /// walkthrough, Home (with the dictation panel), clipboard shelf and Settings. Read on the UI thread because
    /// WinForms handles may not be touched from the capture thread.
    /// </summary>
    private IReadOnlyList<OwnWindow> OwnWindowsForCapture()
    {
        var windows = new List<OwnWindow>();
        _uiContext.Send(_ =>
        {
            void Add(Form? form)
            {
                if (form is { IsDisposed: false, IsHandleCreated: true } && IsWindowVisible(form.Handle)) windows.Add(new(form.Handle, false));
            }
            if (_shellSurface is { Window: { IsDisposed: false, IsHandleCreated: true } shell } && IsWindowVisible(shell.Handle)) windows.Add(new(shell.Handle, false));
            foreach (var hwnd in new[] { _overlay.WindowHandle, _overlay.BuddyTipHandle })
                if (hwnd != 0) windows.Add(new(hwnd, true));
            Add(_companionDock);
            Add(_walkthroughForm);
            Add(_home);
            Add(_clipboardShelf);
            Add(_settingsForm);
        }, null);
        return windows;
    }
}
