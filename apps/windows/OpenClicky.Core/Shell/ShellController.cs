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
    private OnboardingView? _onboarding;
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

    /// <summary>Opens the notch peek (tray, second instance, taskbar button), optionally on a tab and settings section.</summary>
    public void OpenNotch(NotchTab? tab = null, string? section = null)
    {
        SetNotch(_notch with { Tab = tab ?? _notch.Tab, Open = true, Section = section is not null && ShellContract.Sections.Contains(section) ? section : _notch.Section });
        if (_notch.Tab == NotchTab.Settings) _host.SettingsSection(_notch.Section);
        RequestPush();
    }

    /// <summary>The microphone test level (0–1) for the settings page; dropped when the surface is not up.</summary>
    public void PostMicLevel(double level)
    {
        if (_surface is null) return;
        try { _surface.Post(JsonSerializer.Serialize(new { kind = "mic.level", level = double.IsFinite(level) ? Math.Clamp(level, 0, 1) : 0 }, ShellContract.Options)); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { _host.Trace("shell_level_error=" + ex.GetType().Name); }
    }

    /// <summary>Text mode: asks the page to focus the chat field (the page holds it until the Chat tab is shown).</summary>
    public void PostComposerFocus()
    {
        if (_surface is null) return;
        try { _surface.Post("""{"kind":"composer.focus"}"""); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { _host.Trace("shell_focus_error=" + ex.GetType().Name); }
    }

    public void SetUpdate(ShellUpdate update)
    {
        _update = update;
        RequestPush();
    }

    /// <summary>The first-run tour to show (null: none runs).</summary>
    public void SetOnboarding(OnboardingView? view)
    {
        _onboarding = view;
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
        AddCard(new NotchCard("toast-" + Guid.NewGuid().ToString("N"), NotchCardKind.Toast, Cut(text, 160),
            text.Length > 160 ? Cut(text, 1_000) : null, [], ToastMs));
    }

    /// <summary>Shows a card newest first; a card with the same id is replaced (and moves to the front).</summary>
    public void AddCard(NotchCard card)
    {
        _cards.RemoveAll(c => c.Card.Id == card.Id);
        _cards.Insert(0, (card, card.AutoDismissMs is { } ms ? DateTimeOffset.UtcNow.AddMilliseconds(ms) : null));
        if (_cards.Count > MaxCards) _cards.RemoveAt(_cards.Count - 1);
        RequestPush();
    }

    /// <summary>Replaces a card that is still shown, keeping its place and expiry; nothing happens once it is gone.</summary>
    public void UpdateCard(NotchCard card)
    {
        var index = _cards.FindIndex(c => c.Card.Id == card.Id);
        if (index < 0) return;
        _cards[index] = (card, _cards[index].Expires);
        RequestPush();
    }

    public void RemoveCard(string id)
    {
        if (_cards.RemoveAll(c => c.Card.Id == id) > 0) RequestPush();
    }

    /// <summary>A spoken word for the captions bar (at most 200 chars); dropped when the surface is not up.</summary>
    public void PostCaptionWord(string word)
    {
        if (_surface is null || string.IsNullOrWhiteSpace(word)) return;
        try { _surface.Post(JsonSerializer.Serialize(new { kind = "caption.word", text = Cut(word.Trim(), 200) }, ShellContract.Options)); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { _host.Trace("shell_caption_error=" + ex.GetType().Name); }
    }

    /// <summary>The reply finished: the page fades the captions out 1.5 s later unless another word arrives.</summary>
    public void ClearCaptions()
    {
        if (_surface is null) return;
        try { _surface.Post("""{"kind":"caption.clear"}"""); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { _host.Trace("shell_caption_error=" + ex.GetType().Name); }
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
            _last = ShellStateBuilder.Build(inputs) with { Onboarding = _onboarding };
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
                if (_notch.Tab == NotchTab.Settings) _host.SettingsSection(_notch.Section);
                RequestPush();
                break;
            case "settings.section":
                SetNotch(_notch with { Section = intent.GetString("section")! });
                _host.SettingsSection(_notch.Section);
                RequestPush();
                break;
            // The host validates, persists and re-pushes (also on rejection, so the page reverts).
            case "settings.patch": _host.PatchSetting(intent.GetString("key")!, intent.Payload.GetProperty("value")); break;
            case "settings.action": _host.SettingsAction(intent.GetString("action")!, intent.GetString("arg")); break;
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
            case "drawing.action":
                // Only a drawing the page was just shown (a row in the last pushed state) reaches the host; the action is allow-listed by the parser.
                if (_last?.Chat.Rows.Any(r => r.DrawingId == id) != true) { _host.Trace("shell drawing action refused: not in the current state"); break; }
                _host.DrawingAction(id!, intent.GetString("action")!);
                break;
            case "onboarding": _host.OnboardingAction(intent.GetString("action")!); break;
            case "card.dismiss":
                var dismissed = intent.GetString("cardId")!;
                var wasShown = _cards.Any(c => c.Card.Id == dismissed);
                RemoveCard(dismissed);
                if (wasShown) _host.CardDismissed(dismissed);
                break;
            case "card.action":
                // Only an action the card actually offers reaches the host; anything else (a toast, a stale card) just dismisses.
                var cardId = intent.GetString("cardId")!;
                var actionId = intent.GetString("actionId")!;
                var offered = _cards.Any(c => c.Card.Id == cardId && c.Card.Actions.Any(a => a.Id == actionId));
                if (!offered) _host.Trace("shell card action not offered: " + Cut(actionId, 40));
                if (!offered || !_host.CardAction(cardId, actionId)) RemoveCard(cardId);
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
