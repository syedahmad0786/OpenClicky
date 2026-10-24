using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using OpenClicky.App.Surfaces;

namespace OpenClicky.Surface.Smoke;

/// <summary>
/// Spike demo: serves one static page from a loopback HttpListener and shows it in a <see cref="ShellSurfaceWindow"/>
/// over the primary monitor. Logs page messages and hit regions to stdout. Close with Ctrl+C or by killing the process.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--highlight-compositor")) return HighlightCompositorCheck();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();

        var port = FreeLoopbackPort();
        var origin = new Uri($"http://127.0.0.1:{port}/");
        using var listener = new HttpListener();
        listener.Prefixes.Add(origin.AbsoluteUri);
        listener.Start();
        _ = ServeAsync(listener);

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        using var surface = new ShellSurfaceWindow(origin, token, pageMessageKinds: ["demo.click", "demo.input"])
        {
            ExcludeFromCapture = args.Contains("--exclude-from-capture"),
        };
        surface.PageMessage += (_, e) => Log($"page {e.Kind} {e.Json}");
        surface.PageReady += (_, _) => Log("page ready");

        IReadOnlyList<OpenClicky.Core.Surfaces.DeviceRegionRect>? lastRegions = null;
        using var regionTimer = new System.Windows.Forms.Timer { Interval = 250 };
        regionTimer.Tick += (_, _) =>
        {
            if (ReferenceEquals(surface.HitRegions, lastRegions)) return;
            lastRegions = surface.HitRegions;
            Log($"regions {string.Join(" ", lastRegions.Select(r => $"[{r.X},{r.Y} {r.Width}x{r.Height} r{r.Radius}]"))}");
        };
        regionTimer.Start();

        surface.Shown += async (_, _) =>
        {
            try
            {
                await surface.InitializeAsync(Path.Combine(Path.GetTempPath(), "OpenClicky.Surface.Smoke.WebView2"));
                Log($"surface hwnd=0x{surface.Handle.ToInt64():X} bounds={surface.Bounds} dpi={surface.DeviceDpi} origin={origin}");
            }
            catch (Exception ex)
            {
                Log($"init failed: {ex}");
                Application.Exit();
            }
        };
        Application.Run(surface);
        return 0;
    }

    private static void Log(string line) => Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {line}");

    /// <summary>
    /// The highlight compositor paints the strokes and rest rings into the screenshot in the accent, leaves the rest of the
    /// image alone, keeps its size, and returns a screen without marks untouched. In memory only; nothing is shown.
    /// </summary>
    private static int HighlightCompositorCheck()
    {
        byte[] Jpeg()
        {
            using var white = new Bitmap(200, 100);
            using (var g = Graphics.FromImage(white)) g.Clear(Color.White);
            using var stream = new MemoryStream();
            white.Save(stream, System.Drawing.Imaging.ImageFormat.Jpeg);
            return stream.ToArray();
        }
        OpenClicky.Core.ScreenCaptureResult[] screens =
        [
            new(Jpeg(), 200, 100, 0, 0, 400, 200, true, "screen 1 of 2"),
            new(Jpeg(), 200, 100, 400, 0, 400, 200, false, "screen 2 of 2"),
        ];
        var gesture = new OpenClicky.Core.CompanionCursor.HighlightGesture(
            [
                new(OpenClicky.Core.CompanionCursor.StrokeKind.LineAcross, [new(40, 100), new(360, 100)], new(40, 100, 320, 0), []),
                // Its centre is on screen 1, so it is drawn there only (as it is described), though it runs onto screen 2.
                new(OpenClicky.Core.CompanionCursor.StrokeKind.LineAcross, [new(300, 150), new(480, 150)], new(300, 150, 180, 0), []),
            ],
            [new(200, 160)], DateTimeOffset.UtcNow);
        var result = OpenClicky.App.HighlightCompositor.Composite(screens, gesture, "#FF4C4C");
        using var stream = new MemoryStream(result[0].JpegBytes);
        using var image = new Bitmap(stream);
        bool Red(int x, int y) { var c = image.GetPixel(x, y); return c.R > 200 && c.G < 200 && c.R - c.G > 50; }
        bool White(int x, int y) { var c = image.GetPixel(x, y); return c.R > 235 && c.G > 235 && c.B > 235; }
        var failure = !ReferenceEquals(result[1], screens[1]) ? "a screen holding no mark's centre was re-encoded"
            : result[0].PixelWidth != 200 || result[0].PixelHeight != 100 || image.Width != 200 ? "the screenshot changed size"
            : !Red(100, 50) || !Red(30, 50) || !Red(160, 75) ? "a stroke is missing"
            : !Red(110, 80) || !White(100, 80) ? "the rest ring is missing or filled"
            : !White(100, 20) || !White(190, 50) ? "paint outside the marks" : null;
        Console.WriteLine(failure is null ? "PASS: highlight compositor" : "FAIL: highlight compositor " + failure);
        return failure is null ? 0 : 1;
    }

    private static int FreeLoopbackPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static async Task ServeAsync(HttpListener listener)
    {
        var page = Encoding.UTF8.GetBytes(Page);
        while (listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync(); }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { return; }
            using var response = context.Response;
            if (context.Request.HttpMethod != "GET" || context.Request.Url?.AbsolutePath != "/")
            {
                response.StatusCode = 404;
                continue;
            }
            response.ContentType = "text/html; charset=utf-8";
            response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'";
            await response.OutputStream.WriteAsync(page);
        }
    }

    private const string Page = """
        <!doctype html>
        <html><head><meta charset="utf-8"><title>OpenClicky surface smoke</title>
        <style>
          html, body { margin: 0; height: 100%; background: transparent; overflow: hidden; font: 14px "Segoe UI", sans-serif; color: #f4f4f6; }
          #panel { position: fixed; top: 10px; left: 50%; transform: translateX(-50%); width: 460px; height: 64px; box-sizing: border-box;
                   padding: 12px 16px; display: flex; gap: 10px; align-items: center; border-radius: 20px; background: rgba(18, 18, 22, 0.94); }
          #panel button { height: 36px; padding: 0 16px; border: 0; border-radius: 10px; background: #6b5cff; color: #fff; font: inherit; cursor: pointer; }
          #panel input { flex: 1; height: 36px; padding: 0 10px; border: 1px solid #3a3a44; border-radius: 10px; background: #26262e; color: #fff; font: inherit; }
          #status { min-width: 70px; opacity: .8; }
          #card { position: fixed; left: 58%; top: 38%; width: 320px; height: 190px; padding: 20px; box-sizing: border-box; transform: rotate(-8deg);
                  border-radius: 18px; background: rgba(110, 80, 255, 0.45); border: 1px solid rgba(255,255,255,.35); font-size: 18px; }
        </style></head>
        <body>
          <div id="panel" data-hit>
            <button id="ping">Ping</button>
            <input id="text" placeholder="Type here" autocomplete="off">
            <span id="status">clicks: 0</span>
          </div>
          <div id="card" data-hit="box">Translucent tilted card<br><small>the desktop should show through me</small></div>
        <script>
          const post = (m) => window.chrome.webview.postMessage(m);
          let clicks = 0;
          function regions() {
            const rects = [...document.querySelectorAll('[data-hit]')].map(el => {
              const b = el.getBoundingClientRect();
              // A rotated element's client rect is its axis-aligned bounding box, so it gets square corners.
              const r = el.dataset.hit === 'box' ? 0 : parseFloat(getComputedStyle(el).borderTopLeftRadius) || 0;
              return { x: b.x, y: b.y, w: b.width, h: b.height, r };
            });
            post({ kind: 'surface.regions', rects });
          }
          window.chrome.webview.addEventListener('message', (e) => {
            if (e.data && e.data.kind === 'session.bootstrap') { post({ kind: 'ready' }); regions(); }
          });
          new ResizeObserver(regions).observe(document.body);
          document.getElementById('ping').addEventListener('click', () => {
            clicks++;
            document.getElementById('status').textContent = 'clicks: ' + clicks;
            post({ kind: 'demo.click', count: clicks });
          });
          const input = document.getElementById('text');
          input.addEventListener('pointerdown', () => post({ kind: 'surface.focus', wanted: true }));
          input.addEventListener('input', () => post({ kind: 'demo.input', value: input.value }));
          input.addEventListener('keydown', (e) => { if (e.key === 'Escape') { input.blur(); post({ kind: 'surface.focus', wanted: false }); } });
        </script>
        </body></html>
        """;
}
