using System.Runtime.InteropServices;

namespace OpenClicky.Overlay;

/// <summary>
/// A topmost, click-through, non-activating per-pixel-alpha window. Its content is whatever bitmap was last
/// <see cref="Present"/>ed; it never paints itself. UI thread only.
/// </summary>
internal sealed class LayeredWindow : NativeWindow, IDisposable
{
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsExLayered = 0x00080000, WsExTransparent = 0x00000020, WsExToolWindow = 0x00000080,
        WsExNoActivate = 0x08000000, WsExTopmost = 0x00000008;
    private const int UlwAlpha = 2;
    private const byte AcSrcOver = 0, AcSrcAlpha = 1;
    private const int SwHide = 0;
    private const uint SwpNoSize = 0x0001, SwpNoMove = 0x0002, SwpNoActivate = 0x0010, SwpShowWindow = 0x0040;
    private static readonly nint HwndTopMost = -1;

    private readonly Action<string>? _log;
    private readonly string _title;
    private bool _failing;

    /// <param name="log">Diagnostics: the window title and Win32 error codes only, never content.</param>
    /// <param name="owner">An owner window (replay): this one then always stays above it in z-order and goes with it.</param>
    public LayeredWindow(string title, Action<string>? log = null, nint owner = 0)
    {
        _log = log;
        _title = title;
        CreateHandle(new CreateParams
        {
            Caption = title,
            Parent = owner,
            Style = WsPopup,
            ExStyle = WsExLayered | WsExTransparent | WsExToolWindow | WsExNoActivate | WsExTopmost,
        });
    }

    /// <summary>Shows <paramref name="bitmap"/> (32 bpp with alpha) with its top-left at <paramref name="screenTopLeft"/> (virtual-screen px), faded by <paramref name="opacity"/> (0..255).</summary>
    public void Present(Bitmap bitmap, Point screenTopLeft, byte opacity = 255)
    {
        // GDI+ hands back a premultiplied 32 bpp DIB, which is what ULW_ALPHA expects. Taken first: if it throws, no
        // GDI handle is held yet.
        var hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
        nint screenDc = 0, memDc = 0, old = 0;
        bool updated;
        var error = 0;
        try
        {
            screenDc = GetDC(0);
            memDc = CreateCompatibleDC(screenDc);
            old = SelectObject(memDc, hBitmap);
            var size = new NativeSize(bitmap.Width, bitmap.Height);
            var source = new NativePoint(0, 0);
            var destination = new NativePoint(screenTopLeft.X, screenTopLeft.Y);
            var blend = new BlendFunction { BlendOp = AcSrcOver, SourceConstantAlpha = opacity, AlphaFormat = AcSrcAlpha };
            updated = UpdateLayeredWindow(Handle, screenDc, ref destination, ref size, memDc, ref source, 0, ref blend, UlwAlpha);
            if (!updated) error = Marshal.GetLastWin32Error();
        }
        finally
        {
            if (old != 0) SelectObject(memDc, old);
            if (memDc != 0) DeleteDC(memDc);
            if (screenDc != 0) ReleaseDC(0, screenDc);
            DeleteObject(hBitmap);
        }

        // One line per failure streak: this runs every frame.
        if (!updated && !_failing) _log?.Invoke($"overlay_present_failed window={_title} win32={error}");
        _failing = !updated;

        if (!IsWindowVisible(Handle))
            SetWindowPos(Handle, HwndTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
    }

    public void HideWindow()
    {
        if (Handle != 0 && IsWindowVisible(Handle)) ShowWindow(Handle, SwHide);
    }

    public void Dispose() => DestroyHandle();

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint(int x, int y) { public int X = x, Y = y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeSize(int cx, int cy) { public int Cx = cx, Cy = cy; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BlendFunction { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(nint hwnd, nint hdcDst, ref NativePoint pptDst, ref NativeSize psize,
        nint hdcSrc, ref NativePoint pptSrc, int crKey, ref BlendFunction pblend, int dwFlags);
    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint hdc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint hdc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint hdc);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint hdc, nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int cmd);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
}
