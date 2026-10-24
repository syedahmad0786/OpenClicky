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
          input.addEventListener('pointerdown', () => post({ kind: 'surface.focus', active: true }));
          input.addEventListener('input', () => post({ kind: 'demo.input', value: input.value }));
          input.addEventListener('keydown', (e) => { if (e.key === 'Escape') { input.blur(); post({ kind: 'surface.focus', active: false }); } });
        </script>
        </body></html>
        """;
}
