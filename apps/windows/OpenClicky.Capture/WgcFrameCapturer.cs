using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;
using MapFlags = Vortice.Direct3D11.MapFlags;
using MapMode = Vortice.Direct3D11.MapMode;

namespace OpenClicky.Capture;

/// <summary>
/// One-shot monitor snapshot via Windows.Graphics.Capture + Direct3D11CaptureFramePool.
/// </summary>
internal sealed class WgcFrameCapturer : IDisposable
{
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly IDirect3DDevice _winrtDevice;
    private bool _disposed;

    private WgcFrameCapturer(ID3D11Device device, ID3D11DeviceContext context, IDirect3DDevice winrtDevice)
    {
        _device = device;
        _context = context;
        _winrtDevice = winrtDevice;
    }

    public static bool TryCreate(out WgcFrameCapturer? capturer, out string? error)
    {
        capturer = null;
        error = null;
        try
        {
            if (!GraphicsCaptureSession.IsSupported())
            {
                error = "GraphicsCaptureSession.IsSupported() == false";
                return false;
            }

            var levels = new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_0 };
            var hr = D3D11.D3D11CreateDevice(
                IntPtr.Zero,
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport,
                levels,
                out var device,
                out var context);

            if (hr.Failure || device is null || context is null)
            {
                hr = D3D11.D3D11CreateDevice(
                    IntPtr.Zero,
                    DriverType.Warp,
                    DeviceCreationFlags.BgraSupport,
                    levels,
                    out device,
                    out context);
            }

            if (hr.Failure || device is null || context is null)
            {
                error = $"D3D11CreateDevice failed: {hr}";
                return false;
            }

            using var dxgi = device.QueryInterface<IDXGIDevice>();
            // P/Invoke + MarshalInterface.FromAbi yields a real WinRT RCW.
            // Vortice's generic CreateDirect3D11DeviceFromDXGIDevice<T> returns a bare __ComObject
            // that fails CCW when passed to Direct3D11CaptureFramePool on .NET 8.
            var bridgeHr = NativeMethods.CreateDirect3D11DeviceFromDXGIDevice(dxgi.NativePointer, out var punk);
            if (bridgeHr != 0 || punk == IntPtr.Zero)
            {
                device.Dispose();
                context.Dispose();
                error = $"CreateDirect3D11DeviceFromDXGIDevice failed: 0x{bridgeHr:X8}";
                return false;
            }

            IDirect3DDevice winrtDevice;
            try
            {
                winrtDevice = MarshalInterface<IDirect3DDevice>.FromAbi(punk);
            }
            finally
            {
                Marshal.Release(punk);
            }

            capturer = new WgcFrameCapturer(device, context, winrtDevice);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public (byte[] jpeg, int pixelWidth, int pixelHeight) CaptureMonitorJpeg(
        Screen screen,
        int maxEdge,
        TimeSpan timeout)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var bounds = screen.Bounds;
        var hmonitor = NativeMethods.MonitorFromRect(bounds, NativeMethods.MonitorDefaultToNearest);
        if (hmonitor == IntPtr.Zero)
        {
            throw new InvalidOperationException($"MonitorFromRect failed for {bounds}");
        }

        var item = GraphicsCaptureItemFactory.CreateForMonitor(hmonitor);
        var size = item.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            throw new InvalidOperationException("GraphicsCaptureItem reported empty size.");
        }

        using var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _winrtDevice,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            2,
            size);
        using var session = framePool.CreateCaptureSession(item);
        try
        {
            session.IsCursorCaptureEnabled = true;
        }
        catch
        {
            // older builds
        }

        using var arrived = new ManualResetEventSlim(false);
        Direct3D11CaptureFrame? frame = null;
        framePool.FrameArrived += (_, _) =>
        {
            try
            {
                frame ??= framePool.TryGetNextFrame();
            }
            catch
            {
                // ignore transient frame pool races
            }
            finally
            {
                arrived.Set();
            }
        };

        session.StartCapture();
        if (!arrived.Wait(timeout) || frame is null)
        {
            frame?.Dispose();
            throw new TimeoutException($"WGC frame timeout after {timeout.TotalMilliseconds:0}ms");
        }

        using (frame)
        {
            return CopyFrameToJpeg(frame, maxEdge);
        }
    }

    private (byte[] jpeg, int pixelWidth, int pixelHeight) CopyFrameToJpeg(
        Direct3D11CaptureFrame frame,
        int maxEdge)
    {
        var surface = frame.Surface;
        using var texture = GetTextureFromSurface(surface);
        var desc = texture.Description;
        var stagingDesc = new Texture2DDescription
        {
            Width = desc.Width,
            Height = desc.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = desc.Format,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None,
        };

        using var staging = _device.CreateTexture2D(stagingDesc);
        _context.CopyResource(staging, texture);
        var mapped = _context.Map(staging, 0, MapMode.Read, MapFlags.None);
        try
        {
            var width = (int)desc.Width;
            var height = (int)desc.Height;
            var srcStride = (int)mapped.RowPitch;
            var bgra = new byte[checked(height * srcStride)];
            Marshal.Copy(mapped.DataPointer, bgra, 0, bgra.Length);

            var content = frame.ContentSize;
            var cropW = content.Width > 0 ? Math.Min(content.Width, width) : width;
            var cropH = content.Height > 0 ? Math.Min(content.Height, height) : height;
            if (cropW != width || cropH != height)
            {
                var tight = new byte[checked(cropH * cropW * 4)];
                for (var y = 0; y < cropH; y++)
                {
                    Buffer.BlockCopy(bgra, y * srcStride, tight, y * cropW * 4, cropW * 4);
                }

                return CaptureImageEncoder.BgraToJpeg(tight, cropW, cropH, cropW * 4, maxEdge);
            }

            return CaptureImageEncoder.BgraToJpeg(bgra, width, height, srcStride, maxEdge);
        }
        finally
        {
            _context.Unmap(staging, 0);
        }
    }


    private static ID3D11Texture2D GetTextureFromSurface(IDirect3DSurface surface)
    {
        // CsWinRT object → ABI pointer → Vortice IDirect3DDxgiInterfaceAccess → ID3D11Texture2D
        var winrtObject = (IWinRTObject)surface;
        var abi = winrtObject.NativeObject.ThisPtr;
        using var access = (IDirect3DDxgiInterfaceAccess)abi;
        return access.GetInterface<ID3D11Texture2D>();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _context.Dispose();
        _device.Dispose();
        // Proper WinRT RCW implements IDisposable; bare __ComObject does not (seen on CI).
        try
        {
            (_winrtDevice as IDisposable)?.Dispose();
        }
        catch
        {
            // best-effort
        }
    }

    private static class NativeMethods
    {
        [DllImport("d3d11.dll", ExactSpelling = true)]
        public static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

        public const uint MonitorDefaultToNearest = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        public static IntPtr MonitorFromRect(Rectangle bounds, uint flags)
        {
            var rect = new Rect
            {
                Left = bounds.Left,
                Top = bounds.Top,
                Right = bounds.Right,
                Bottom = bounds.Bottom,
            };
            return MonitorFromRect(ref rect, flags);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromRect(ref Rect rect, uint flags);
    }
}
