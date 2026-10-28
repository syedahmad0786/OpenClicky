using OpenClicky.Capture;

// Headless capture smoke for windows-latest CI / local Win11.
// Does NOT exercise tray, PTT, overlay, or OpenRouter — only capture backends.
if (args.Contains("--own-windows")) return await OwnWindowsSmoke.RunAsync(args);

Console.WriteLine("OpenClicky capture smoke starting…");

var capture = new WgcScreenCaptureService(msg => Console.WriteLine(msg));
try
{
    var results = await capture.CaptureAllMonitorsJpegAsync();
    if (results.Count == 0)
    {
        Console.Error.WriteLine("FAIL: no monitors captured");
        return 1;
    }

    var primary = results.FirstOrDefault(r => r.IsCursorMonitor) ?? results[0];
    Console.WriteLine(
        $"monitors={results.Count} backend={capture.LastBackend} wgc_supported={capture.GraphicsCaptureSupported} " +
        $"bytes={primary.JpegBytes.Length} jpeg={primary.LooksLikeJpeg} size={primary.PixelWidth}x{primary.PixelHeight}");

    if (!primary.LooksLikeJpeg || primary.JpegBytes.Length < 100)
    {
        Console.Error.WriteLine("FAIL: capture did not produce a JPEG");
        return 2;
    }

    Console.WriteLine("PASS");
    return 0;
}
finally
{
    try
    {
        capture.Dispose();
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"dispose_warning={ex.Message}");
    }
}
