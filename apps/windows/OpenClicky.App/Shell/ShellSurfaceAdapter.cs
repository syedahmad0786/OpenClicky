using System.Text.Json;
using OpenClicky.App.Surfaces;
using OpenClicky.Core.Shell;

namespace OpenClicky.App.Shell;

/// <summary>
/// <see cref="IShellSurface"/> over the WebView2 <see cref="ShellSurfaceWindow"/> showing <c>shell.html</c>.
/// While the notch is collapsed it follows the cursor's monitor (checked every 500 ms); the settings tab raises
/// the region area cap to the settings envelope because its 920×720 panel exceeds 45% of small monitors.
/// </summary>
internal sealed class ShellSurfaceAdapter : IShellSurface
{
    /// <summary>Page intents forwarded to the controller (the window handles bootstrap/regions/focus itself).</summary>
    private static readonly string[] PageKinds =
    [
        "talk.toggle", "update.check", "update.install", "app.quit", "notch.tab", "notch.open", "text.submit",
        "general.set", "job.open", "job.stop", "job.dismiss", "file.open", "file.reveal", "card.action",
        "card.dismiss", "link.open", "settings.section", "settings.patch", "settings.action", "drawing.action",
    ];

    private readonly System.Windows.Forms.Timer _follow = new() { Interval = 500 };
    private ShellNotch _notch = new(NotchTab.Home, false, ShellContract.DefaultSection);

    private ShellSurfaceAdapter(ShellSurfaceWindow window)
    {
        Window = window;
        window.PageMessage += (_, e) => Message?.Invoke(e.Kind, e.Json);
        window.SurfaceFailed += (_, _) => Failed?.Invoke();
        // Any move or resize of the surface (following the cursor, a display, DPI or layout change refit) moves the notch.
        window.LocationChanged += (_, _) => ScreenChanged?.Invoke();
        window.SizeChanged += (_, _) => ScreenChanged?.Invoke();
        _follow.Tick += (_, _) => FollowCursor();
        _follow.Start();
    }

    public ShellSurfaceWindow Window { get; }
    public event Action<string, string>? Message;
    public event Action? Failed;
    /// <summary>The surface moved or was resized (another monitor, or a display / DPI / layout change): the notch point moved.</summary>
    public event Action? ScreenChanged;

    public static async Task<ShellSurfaceAdapter> CreateAsync(Uri origin, string token, string userDataFolder, bool excludeFromCapture)
    {
        var window = new ShellSurfaceWindow(origin, token, Screen.PrimaryScreen, PageKinds, "shell.html");
        try
        {
            window.Show();
            window.ExcludeFromCapture = excludeFromCapture;
            await window.InitializeAsync(userDataFolder);
            return new ShellSurfaceAdapter(window);
        }
        catch
        {
            window.Dispose();
            throw;
        }
    }

    public void Post(string json)
    {
        using var document = JsonDocument.Parse(json);
        Window.Post(document.RootElement.Clone());
    }

    public void SetNotch(ShellNotch notch)
    {
        _notch = notch;
        Window.AllowLargeRegions = notch.Open && notch.Tab == NotchTab.Settings;
    }

    private void FollowCursor()
    {
        if (_notch.Open || Window.IsDisposed) return;
        var screen = Screen.FromPoint(Cursor.Position);
        if (!Window.Bounds.Equals(screen.Bounds)) Window.MoveToScreen(screen); // raises LocationChanged / SizeChanged
    }

    public void Dispose()
    {
        _follow.Dispose();
        Window.Dispose();
    }
}
