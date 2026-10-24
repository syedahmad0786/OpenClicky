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

    public LayeredWindow(string title)
    {
        CreateHandle(new CreateParams
        {
            Caption = title,
            Style = WsPopup,
            ExStyle = WsExLayered | WsExTransparent | WsExToolWindow | WsExNoActivate | WsExTopmost,
        });
    }

    public bool Visible => Handle != 0 && IsWindowVisible(Handle);

    /// <summary>Shows <paramref name="bitmap"/> (32 bpp with alpha) with its top-left at <paramref name="screenTopLeft"/> (virtual-screen px).</summary>
    public void Present(Bitmap bitmap, Point screenTopLeft)
    {
        var screenDc = GetDC(0);
        var memDc = CreateCompatibleDC(screenDc);
        // GDI+ hands back a premultiplied 32 bpp DIB, which is what ULW_ALPHA expects.
        var hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
        var old = SelectObject(memDc, hBitmap);
        try
        {
            var size = new NativeSize(bitmap.Width, bitmap.Height);
            var source = new NativePoint(0, 0);
            var destination = new NativePoint(screenTopLeft.X, screenTopLeft.Y);
            var blend = new BlendFunction { BlendOp = AcSrcOver, SourceConstantAlpha = 255, AlphaFormat = AcSrcAlpha };
            UpdateLayeredWindow(Handle, screenDc, ref destination, ref size, memDc, ref source, 0, ref blend, UlwAlpha);
        }
        finally
        {
            SelectObject(memDc, old);
            DeleteObject(hBitmap);
            DeleteDC(memDc);
            ReleaseDC(0, screenDc);
        }

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
