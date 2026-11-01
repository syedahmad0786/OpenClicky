using OpenClicky.Core;
using OpenClicky.Core.Drawing;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;

namespace OpenClicky.App;

/// <summary>
/// Windows' built-in OCR over the screenshots the model was given, so labelled marks can be fitted to the words they name.
/// Each image is enlarged 2× first (downscaled UI text reads far better), and boxes come back in the screenshot's pixels.
/// Runs on this PC only; nothing is sent anywhere.
/// </summary>
internal static class ScreenTextReader
{
    private const double Enlarge = 2;

    public static async Task<IReadOnlyList<ScreenText>> ReadAsync(IReadOnlyList<ScreenCaptureResult> screens, CancellationToken token)
    {
        if (OcrEngine.TryCreateFromUserProfileLanguages() is not { } engine) return [];
        var found = new List<ScreenText>();
        for (var i = 0; i < screens.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var shot = screens[i];
            if (!shot.LooksLikeJpeg || shot.PixelWidth <= 0 || shot.PixelHeight <= 0) continue;
            var scale = Math.Min(Enlarge, OcrEngine.MaxImageDimension / (double)Math.Max(shot.PixelWidth, shot.PixelHeight));
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(CryptographicBuffer.CreateFromByteArray(shot.JpegBytes));
            stream.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var transform = new BitmapTransform
            {
                ScaledWidth = (uint)Math.Max(1, shot.PixelWidth * scale),
                ScaledHeight = (uint)Math.Max(1, shot.PixelHeight * scale),
                InterpolationMode = BitmapInterpolationMode.Fant,
            };
            using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
                ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
            token.ThrowIfCancellationRequested();
            var result = await engine.RecognizeAsync(bitmap);
            foreach (var line in result.Lines)
            {
                if (line.Words.Count == 0 || string.IsNullOrWhiteSpace(line.Text)) continue;
                double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
                foreach (var word in line.Words)
                {
                    var r = word.BoundingRect;
                    left = Math.Min(left, r.X); top = Math.Min(top, r.Y);
                    right = Math.Max(right, r.X + r.Width); bottom = Math.Max(bottom, r.Y + r.Height);
                }
                found.Add(new(i + 1, line.Text, new((float)(left / scale), (float)(top / scale), (float)((right - left) / scale), (float)((bottom - top) / scale))));
            }
        }
        return found;
    }
}
