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
    private const int MaxDelta = 8_000, MaxLiveRows = 40;

    private readonly IShellHost _host;
    private readonly Func<Task<IShellSurface>> _surfaceFactory;
    private readonly Action<Action> _schedule;
    private readonly HashSet<string> _dismissed = new(StringComparer.Ordinal);
    private readonly List<ChatRow> _liveRows = [];
    private IShellSurface? _surface;
    private ShellState? _last;
    private ShellNotch _notch = new(NotchTab.Home, false);
    private string? _featuredJobId;
    private ShellUpdate? _update;
    private bool _pushPending;

    /// <param name="schedulePush">Runs the given push later on the UI thread; called at most once per pending push.</param>
    public ShellController(IShellHost host, Func<Task<IShellSurface>> surfaceFactory, Action<Action> schedulePush)
    {
        _host = host;
        _surfaceFactory = surfaceFactory;
        _schedule = schedulePush;
    }

    public bool IsActive => _surface is not null;
    public ShellNotch Notch => _notch;

    /// <summary>Creates the surface and pushes the first state. On any failure the host shows the classic window.</summary>
    public async Task<bool> StartAsync()
    {
        try
        {
            _surface = await _surfaceFactory();
        }
        catch (Exception ex)
        {
            _host.Trace("shell_start_error=" + ex.GetType().Name + ": " + ex.Message);
            _host.ShowClassic();
            return false;
        }
        _surface.Message += OnMessage;
        _surface.SetNotch(_notch);
        PushNow();
        return true;
    }

    /// <summary>Opens the notch peek (tray, second instance, taskbar button).</summary>
    public void OpenNotch(NotchTab? tab = null)
    {
        SetNotch(new ShellNotch(tab ?? _notch.Tab, true));
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
        if (_surface is not null && !_pushPending && text.StartsWith(row.Text, StringComparison.Ordinal))
            for (var at = row.Text.Length; at < text.Length; at += MaxDelta)
                _surface.Post(JsonSerializer.Serialize(new { kind = "chat.delta", rowId = row.Id, text = text[at..Math.Min(text.Length, at + MaxDelta)] }, ShellContract.Options));
        else RequestPush();
    }

    /// <summary>The turn was saved to history, which now carries its rows.</summary>
    public void EndTurn()
    {
        _liveRows.Clear();
        RequestPush();
    }

    private void SetNotch(ShellNotch notch)
    {
        _notch = notch;
        _surface?.SetNotch(notch);
    }

    private void PushNow()
    {
        if (_surface is null) return;
        try
        {
            var inputs = _host.CollectInputs() with
            {
                Notch = _notch,
                FeaturedJobId = _featuredJobId,
                DismissedJobIds = _dismissed,
                LiveRows = _liveRows.ToList(),
                Update = _update,
            };
            _last = ShellStateBuilder.Build(inputs);
            _surface.Post(JsonSerializer.Serialize(new { kind = "shell.state", state = _last }, ShellContract.Options));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _host.Trace("shell_push_error=" + ex.GetType().Name);
        }
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
            case "ready": PushNow(); break;
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
                SetNotch(new ShellNotch(NotchTab.Home, true));
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
                if (intent.Kind == "file.open") _host.OpenFile(path); else _host.RevealFile(path);
                break;
            case "update.check": _host.CheckForUpdates(); break;
            case "update.install": _host.InstallUpdate(); break;
            case "link.open":
                _host.OpenUrl(intent.GetString("target") switch { "whatsNew" => WhatsNewUrl, "bug" => BugUrl, _ => FeatureUrl });
                break;
            case "app.quit": _host.Quit(); break;
            default: _host.Trace("shell intent not handled yet: " + intent.Kind); break; // card.* (no host cards yet)
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
        if (_surface is null) return;
        _surface.Message -= OnMessage;
        _surface.Dispose();
        _surface = null;
    }
}
