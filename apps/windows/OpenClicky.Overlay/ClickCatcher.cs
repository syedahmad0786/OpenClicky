using System.Runtime.InteropServices;

namespace OpenClicky.Overlay;

/// <summary>
/// A tiny topmost, non-activating window at alpha 1/255: invisible, but (unlike the click-through overlay windows) it
/// receives the mouse, so a click on something drawn by the overlay can be noticed. Raises <see cref="Clicked"/> on a
/// full left click (down and up inside it). UI thread only.
/// </summary>
internal sealed class ClickCatcher : NativeWindow, IDisposable
{
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsExLayered = 0x00080000, WsExToolWindow = 0x00000080, WsExNoActivate = 0x08000000, WsExTopmost = 0x00000008;
    private const int WmMouseActivate = 0x21, WmLButtonDown = 0x201, WmLButtonUp = 0x202, MaNoActivate = 3, LwaAlpha = 2;
    private const uint SwpNoActivate = 0x0010, SwpShowWindow = 0x0040;
    private static readonly nint HwndTopMost = -1;
    private bool _down;

    public ClickCatcher(string title)
    {
        CreateHandle(new CreateParams { Caption = title, Style = WsPopup, ExStyle = WsExLayered | WsExToolWindow | WsExNoActivate | WsExTopmost });
        SetLayeredWindowAttributes(Handle, 0, 1, LwaAlpha);
    }

    public event Action? Clicked;

    public bool IsShown => Handle != 0 && IsWindowVisible(Handle);

    /// <summary>Covers <paramref name="bounds"/> (virtual-screen px) without taking focus.</summary>
    public void Show(Rectangle bounds) =>
        SetWindowPos(Handle, HwndTopMost, bounds.X, bounds.Y, Math.Max(1, bounds.Width), Math.Max(1, bounds.Height), SwpNoActivate | SwpShowWindow);

    public void Hide()
    {
        _down = false;
        if (Handle != 0 && IsWindowVisible(Handle)) ShowWindow(Handle, 0);
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WmMouseActivate:
                m.Result = MaNoActivate;
                return;
            case WmLButtonDown:
                _down = true;
                return;
            case WmLButtonUp when _down:
                _down = false;
                Clicked?.Invoke();
                return;
        }
        base.WndProc(ref m);
    }

    public void Dispose() => DestroyHandle();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetLayeredWindowAttributes(nint hwnd, int crKey, byte alpha, int flags);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int cmd);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
}
