using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using WinRT;

namespace OpenClicky.Capture;

/// <summary>Creates <see cref="GraphicsCaptureItem"/> for an HMONITOR without the system picker.</summary>
internal static class GraphicsCaptureItemFactory
{
    private static readonly Guid GraphicsCaptureItemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    public static GraphicsCaptureItem CreateForMonitor(IntPtr hmonitor)
    {
        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        var iid = GraphicsCaptureItemIid;
        var hr = interop.CreateForMonitor(hmonitor, ref iid, out var unk);
        if (hr < 0 || unk == IntPtr.Zero)
        {
            Marshal.ThrowExceptionForHR(hr == 0 ? unchecked((int)0x80004005) : hr);
        }

        try
        {
            return MarshalInterface<GraphicsCaptureItem>.FromAbi(unk);
        }
        finally
        {
            Marshal.Release(unk);
        }
    }

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        [PreserveSig]
        int CreateForWindow(IntPtr window, ref Guid iid, out IntPtr result);

        [PreserveSig]
        int CreateForMonitor(IntPtr monitor, ref Guid iid, out IntPtr result);
    }
}
