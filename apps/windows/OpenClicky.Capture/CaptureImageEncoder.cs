using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace OpenClicky.Capture;

public static class CaptureImageEncoder
{
    public static (byte[] jpeg, int width, int height) BgraToJpeg(
        byte[] bgra,
        int width,
        int height,
        int stride,
        int maxEdge = 1280)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(
            new Rectangle(0, 0, width, height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            // WGC/D3D surfaces are BGRA; Format32bppArgb in GDI+ is also B-G-R-A in memory on LE.
            var destStride = Math.Abs(data.Stride);
            var rowBytes = Math.Min(width * 4, Math.Min(stride, destStride));
            for (var y = 0; y < height; y++)
            {
                Marshal.Copy(
                    bgra,
                    y * stride,
                    IntPtr.Add(data.Scan0, y * destStride),
                    rowBytes);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return BitmapToJpeg(bitmap, maxEdge);
    }

    public static (byte[] jpeg, int width, int height) BitmapToJpeg(Bitmap source, int maxEdge = 1280)
    {
        using var scaled = Downscale(source, maxEdge);
        using var stream = new MemoryStream();
        scaled.Save(stream, ImageFormat.Jpeg);
        return (stream.ToArray(), scaled.Width, scaled.Height);
    }

    public static Bitmap Downscale(Bitmap source, int maxEdge)
    {
        var longEdge = Math.Max(source.Width, source.Height);
        if (longEdge <= maxEdge)
        {
            return new Bitmap(source);
        }

        var scale = maxEdge / (double)longEdge;
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(source, 0, 0, width, height);
        return result;
    }
}
