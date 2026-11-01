using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using OpenClicky.Capture;
using OpenClicky.Core;

/// <summary>
/// Native check for the Talk capture exclusion: a top-most solid-magenta window must show up in a plain all-monitor capture,
/// be absent while DisplayAffinityScope has it excluded, and be back (affinity restored) afterwards.
/// Usage: --own-windows [outputDir]
/// </summary>
internal static class OwnWindowsSmoke
{
    [DllImport("user32.dll")] private static extern bool GetWindowDisplayAffinity(nint hwnd, out uint affinity);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(nint hwnd, uint affinity);

    public static async Task<int> RunAsync(string[] args)
    {
        var outDir = args.SkipWhile(a => a != "--own-windows").Skip(1).FirstOrDefault(a => !a.StartsWith("--")) ?? Path.GetTempPath();
        Directory.CreateDirectory(outDir);
        Application.EnableVisualStyles();
        var cursorScreen = Screen.AllScreens.FirstOrDefault(s => s.Bounds.Contains(Cursor.Position)) ?? Screen.PrimaryScreen!;
        using var form = new Form
        {
            FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, TopMost = true, ShowInTaskbar = false,
            BackColor = Color.FromArgb(255, 0, 255), Text = "OpenClicky own-window smoke",
            Bounds = new Rectangle(cursorScreen.Bounds.Left + 120, cursorScreen.Bounds.Top + 120, 480, 480),
        };
        form.Show();
        var hwnd = form.Handle;
        Pump(400);

        GetWindowDisplayAffinity(hwnd, out var before);
        using var capture = new WgcScreenCaptureService(Console.WriteLine);

        var plain = await capture.CaptureAllMonitorsJpegAsync();
        var plainHits = Save(outDir, "1-plain", plain);

        int excludedHits, changed;
        uint during;
        using (var scope = new DisplayAffinityScope([hwnd],
            h => GetWindowDisplayAffinity(h, out var a) ? a : null, SetWindowDisplayAffinity))
        {
            changed = scope.ChangedCount;
            GetWindowDisplayAffinity(hwnd, out during);
            Pump(120);
            var excluded = await capture.CaptureAllMonitorsJpegAsync();
            excludedHits = Save(outDir, "2-excluded", excluded);
        }

        GetWindowDisplayAffinity(hwnd, out var after);
        Pump(300);
        var restored = await capture.CaptureAllMonitorsJpegAsync();
        var restoredHits = Save(outDir, "3-restored", restored);
        var visible = form.Visible;

        Console.WriteLine($"affinity before=0x{before:X} during=0x{during:X} after=0x{after:X} changed={changed} visible={visible}");
        Console.WriteLine($"magenta pixels plain={plainHits} excluded={excludedHits} restored={restoredHits}");
        var ok = plainHits > 1000 && excludedHits < plainHits / 100 && restoredHits > 1000 && during == 0x11 && after == before && visible && changed == 1;
        Console.WriteLine(ok ? "PASS" : "FAIL");
        return ok ? 0 : 1;
    }

    private static void Pump(int milliseconds)
    {
        var end = Environment.TickCount64 + milliseconds;
        while (Environment.TickCount64 < end) { Application.DoEvents(); Thread.Sleep(10); }
    }

    private static int Save(string dir, string name, IReadOnlyList<ScreenCaptureResult> shots)
    {
        var hits = 0;
        for (var i = 0; i < shots.Count; i++)
        {
            File.WriteAllBytes(Path.Combine(dir, $"{name}-screen{i + 1}.jpg"), shots[i].JpegBytes);
            using var bitmap = new Bitmap(new MemoryStream(shots[i].JpegBytes));
            for (var y = 0; y < bitmap.Height; y++)
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var c = bitmap.GetPixel(x, y);
                    if (c.R > 200 && c.G < 70 && c.B > 200) hits++;
                }
        }
        return hits;
    }
}
