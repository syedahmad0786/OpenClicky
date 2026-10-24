using System.Text.Json;

namespace OpenClicky.Core.Shell;

/// <summary>
/// Owns the shell page's state and routes its intents to the host. Platform-neutral so it is unit-tested;
/// the app supplies a WebView2 surface factory and a UI-thread coalescing scheduler (60 ms).
/// </summary>
public sealed class ShellController : IDisposable
{
    public const string WhatsNewUrl = "https://github.com/syedahmad0786/OpenClicky/releases";
    public const string BugUrl = "https://github.com/syedahmad0786/OpenClicky/issues/new";
    public const string FeatureUrl = "https://github.com/syedahmad0786/OpenClicky/discussions/new?category=ideas";
    private const int MaxDelta = 8_000, MaxLiveRows = 40, MaxCards = 12, ToastMs = 6_000;
    /// <summary>link.open and update.check from the page are ignored when repeated sooner than this.</summary>
    public const long IntentCooldownMs = 2_000;

    /// <summary>Documents and media file.open may open; every other extension (scripts, executables, shortcuts…) is revealed in Explorer.</summary>
    private static readonly HashSet<string> OpenAllowed = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doc", ".docx", ".pdf", ".xls", ".xlsx", ".csv", ".ppt", ".pptx", ".txt", ".md", ".rtf", ".json",
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".svg", ".mp3", ".wav", ".m4a", ".ogg", ".mp4", ".mov", ".webm",
        ".html", ".htm",
    };

    private readonly IShellHost _host;
    private readonly Func<Task<IShellSurface>> _surfaceFactory;
    private readonly Action<Action> _schedule;
    private readonly Func<long> _clock;
    private readonly Dictionary<string, long> _lastIntentAt = new(StringComparer.Ordinal);
    private readonly HashSet<string> _dismissed = new(StringComparer.Ordinal);
    private readonly List<ChatRow> _liveRows = [];
    private readonly List<(NotchCard Card, DateTimeOffset? Expires)> _cards = [];
    private string? _lastCommittedAssistant;
    private bool _disposed;
    private IShellSurface? _surface;
    private ShellState? _last;
    private ShellNotch _notch = new(NotchTab.Home, false, ShellContract.DefaultSection);
    private string? _featuredJobId;
    private ShellUpdate? _update;
    private bool _pushPending;

    /// <param name="schedulePush">Runs the given push later on the UI thread; called at most once per pending push.</param>
    /// <param name="clock">Monotonic milliseconds (tests); defaults to <see cref="Environment.TickCount64"/>.</param>
    public ShellController(IShellHost host, Func<Task<IShellSurface>> surfaceFactory, Action<Action> schedulePush, Func<long>? clock = null)
    {
        _host = host;
        _surfaceFactory = surfaceFactory;
        _schedule = schedulePush;
        _clock = clock ?? (() => Environment.TickCount64);
    }

    public bool IsActive => _surface is not null;
    public ShellNotch Notch => _notch;

    /// <summary>Creates the surface and pushes the first state. On any failure the host shows the classic window.</summary>
    public async Task<bool> StartAsync()
    {
        try
        {
            var surface = await _surfaceFactory();
            if (_disposed) { surface.Dispose(); return false; } // the app quit while the surface was starting
            _surface = surface;
        }
        catch (Exception ex)
        {
            if (_disposed) return false;
            _host.Trace("shell_start_error=" + ex.GetType().Name + ": " + ex.Message);
            _host.ShowClassic();
            return false;
        }
        _surface.Message += OnMessage;
        _surface.Failed += OnSurfaceFailed;
        _surface.SetNotch(_notch);
        if (PushNow()) return true;
        // Without a first state the page stays blank (and invisible): use the classic window instead.
        TearDownSurface();
        _host.ShowClassic();
        return false;
    }

    /// <summary>The surface died (WebView2 browser process gone): drop it and fall back to the classic window.</summary>
    private void OnSurfaceFailed()
    {
        if (_surface is null || _disposed) return;
        _host.Trace("shell_surface_failed");
        TearDownSurface();
        _host.ShowClassic();
    }

    private void TearDownSurface()
    {
        if (_surface is not { } surface) return;
        _surface = null;
        _pushPending = false;
        surface.Message -= OnMessage;
        surface.Failed -= OnSurfaceFailed;
        surface.Dispose();
    }

    /// <summary>Opens the notch peek (tray, second instance, taskbar button).</summary>
    public void OpenNotch(NotchTab? tab = null)
    {
        SetNotch(_notch with { Tab = tab ?? _notch.Tab, Open = true });
        RequestPush();
    }

    public void SetUpdate(ShellUpdate update)
    {
        _update = update;
        RequestPush();
    }

    /// <summary>Coalesces state pushes: many changes inside one scheduler tick produce one post.</summary>
    public void RequestPush()
    {
        if (_surface is null || _pushPending) return;
        _pushPending = true;
        _schedule(() => { _pushPending = false; PushNow(); });
    }

    /// <summary>A row of the turn in progress (shown until the turn lands in history).</summary>
    public void AddLiveRow(ChatRole role, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (role == ChatRole.User) _lastCommittedAssistant = null; // a new turn started
        else if (role == ChatRole.Assistant && text == _lastCommittedAssistant)
        {
            // Already in history: the turn was committed before its reply was reported.
            _lastCommittedAssistant = null;
            return;
        }
        _liveRows.Add(new ChatRow("live-" + Guid.NewGuid().ToString("N"), role, Cut(text, 32_000), ShellStateBuilder.Iso(DateTimeOffset.UtcNow)));
        if (_liveRows.Count > MaxLiveRows) _liveRows.RemoveAt(0);
        RequestPush();
    }

    /// <summary>Streams assistant text (the full text so far). Appends travel as chat.delta; anything else re-pushes state.</summary>
    public void StreamAssistant(string text, bool complete)
    {
        text = Cut(text, 32_000);
        var index = _liveRows.FindLastIndex(r => r.Role == ChatRole.Assistant && r.Id.StartsWith("live-stream-", StringComparison.Ordinal));
        if (index < 0)
        {
            if (!complete && text.Length > 0)
            {
                _liveRows.Add(new ChatRow("live-stream-" + Guid.NewGuid().ToString("N"), ChatRole.Assistant, text, ShellStateBuilder.Iso(DateTimeOffset.UtcNow)));
                RequestPush();
            }
            return;
        }
        var row = _liveRows[index];
        if (complete)
        {
            // The finished reply arrives as its own message; drop the streaming row.
            _liveRows.RemoveAt(index);
            RequestPush();
            return;
        }
        if (text == row.Text) return;
        _liveRows[index] = row with { Text = text };
        if (_surface is not null && !_pushPending && text.Length <= ShellStateBuilder.MaxRowText && text.StartsWith(row.Text, StringComparison.Ordinal))
        {
            try
            {
                for (var at = row.Text.Length; at < text.Length; at += MaxDelta)
                    _surface.Post(JsonSerializer.Serialize(new { kind = "chat.delta", rowId = row.Id, text = text[at..Math.Min(text.Length, at + MaxDelta)] }, ShellContract.Options));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _host.Trace("shell_delta_error=" + ex.GetType().Name);
            }
        }
        else RequestPush();
    }

    /// <summary>The turn was saved to history, which now carries its rows. A later identical assistant message is not repeated.</summary>
    public void EndTurn(string? committedAssistant = null)
    {
        _liveRows.Clear();
        _lastCommittedAssistant = committedAssistant;
        RequestPush();
    }

    /// <summary>Shows a short message as a toast card (newest first, auto-dismissed after 6 s).</summary>
    public void AddToast(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        text = text.Trim();
        var card = new NotchCard("toast-" + Guid.NewGuid().ToString("N"), NotchCardKind.Toast, Cut(text, 160),
            text.Length > 160 ? Cut(text, 1_000) : null, [], ToastMs);
        _cards.Insert(0, (card, DateTimeOffset.UtcNow.AddMilliseconds(ToastMs)));
        if (_cards.Count > MaxCards) _cards.RemoveAt(_cards.Count - 1);
        RequestPush();
    }

    private void SetNotch(ShellNotch notch)
    {
        _notch = notch;
        _surface?.SetNotch(notch);
    }

    /// <summary>Builds and posts the state; false (traced) when collecting, building or posting failed.</summary>
    private bool PushNow()
    {
        if (_surface is null) return false;
        try
        {
            var now = DateTimeOffset.UtcNow;
            _cards.RemoveAll(c => c.Expires <= now);
            var inputs = _host.CollectInputs() with
            {
                Notch = _notch,
                FeaturedJobId = _featuredJobId,
                DismissedJobIds = _dismissed,
                LiveRows = _liveRows.ToList(),
                Update = _update,
                Cards = _cards.Select(c => c.Card).ToList(),
            };
            _last = ShellStateBuilder.Build(inputs);
            _surface.Post(JsonSerializer.Serialize(new { kind = "shell.state", state = _last }, ShellContract.Options));
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _host.Trace("shell_push_error=" + ex.GetType().Name);
            return false;
        }
    }

    /// <summary>False (traced) when the same intent kind arrived less than <see cref="IntentCooldownMs"/> ago.</summary>
    private bool CooledDown(string kind)
    {
        var now = _clock();
        if (_lastIntentAt.TryGetValue(kind, out var last) && now - last < IntentCooldownMs)
        {
            _host.Trace("shell intent rate-limited: " + kind);
            return false;
        }
        _lastIntentAt[kind] = now;
        return true;
    }

    private void OnMessage(string kind, string json)
    {
        if (!ShellIntentParser.TryParse(json, out var intent))
        {
            _host.Trace("shell intent rejected: " + Cut(kind, 40));
            return;
        }
        var id = intent.GetString("id");
        switch (intent.Kind)
        {
            case "ready": _ = PushNow(); break;
            case "talk.toggle": _host.ToggleTalk(); break;
            case "text.submit": _host.SubmitText(intent.GetString("text")!); break;
            case "notch.tab":
                SetNotch(_notch with { Tab = Enum.Parse<NotchTab>(intent.GetString("tab")!, ignoreCase: true) });
                RequestPush();
                break;
            case "notch.open":
                SetNotch(_notch with { Open = intent.GetBool("open") == true });
                RequestPush();
                break;
            case "general.set":
                _host.SetGeneral(intent.GetString("key")!, intent.GetBool("value") == true);
                RequestPush();
                break;
            case "job.open":
                _featuredJobId = id;
                _dismissed.Remove(id!);
                _host.OpenJob(id!);
                SetNotch(_notch with { Tab = NotchTab.Home, Open = true });
                RequestPush();
                break;
            case "job.stop": _host.StopJob(id!); break;
            case "job.dismiss":
                _dismissed.Add(id!);
                if (_featuredJobId == id) _featuredJobId = null;
                RequestPush();
                break;
            case "file.open" or "file.reveal":
                var path = intent.GetString("path")!;
                if (!InLastState(path)) { _host.Trace("shell file intent refused: path not in the current state"); break; }
                if (intent.Kind == "file.open" && OpenAllowed.Contains(Path.GetExtension(path))) _host.OpenFile(path);
                else _host.RevealFile(path);
                break;
            case "update.check": if (CooledDown(intent.Kind)) _host.CheckForUpdates(); break;
            case "update.install": _host.InstallUpdate(); break;
            case "link.open":
                if (!CooledDown(intent.Kind)) break;
                _host.OpenUrl(intent.GetString("target") switch { "whatsNew" => WhatsNewUrl, "bug" => BugUrl, _ => FeatureUrl });
                break;
            case "app.quit": _host.Quit(); break;
            case "card.dismiss" or "card.action":
                // Toasts carry no actions, so an action on any card just dismisses it.
                _cards.RemoveAll(c => c.Card.Id == intent.GetString("cardId"));
                RequestPush();
                break;
            default: _host.Trace("shell intent not handled: " + intent.Kind); break;
        }
    }

    /// <summary>Exact match against paths the host itself published: job files, featured files, the open document.</summary>
    private bool InLastState(string path) =>
        _last is { } state
        && (state.Jobs.Any(j => j.Files.Any(f => f.Path == path))
            || state.Featured?.Files.Any(f => f.Path == path) == true
            || state.Document?.Path == path);

    private static string Cut(string value, int max) => value.Length <= max ? value : value[..max];

    public void Dispose()
    {
        _disposed = true;
        TearDownSurface();
    }
}
