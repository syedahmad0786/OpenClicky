using System.Diagnostics;
using OpenClicky.Agent;
using OpenClicky.App.Proactive;
using OpenClicky.Core;
using OpenClicky.Core.Proactive;
using OpenClicky.Core.Shell;
using OpenClicky.Providers;

namespace OpenClicky.App;

/// <summary>
/// Proactive cards (all off by default): a 2.5 s UI-thread timer feeds the coordinator, which only shows and removes cards.
/// Sending text, opening a URL or opening Settings happens only from <see cref="ProactiveCardAction"/>, the user's click.
/// </summary>
internal sealed partial class TrayApplicationContext
{
    private static readonly TimeSpan ProactiveWorkRefresh = TimeSpan.FromSeconds(60);
    private readonly System.Windows.Forms.Timer _proactiveTimer = new() { Interval = 2_500 };
    private ProactiveCoordinator? _proactive;
    private long _proactiveActivityTick; // Environment.TickCount64 of the last physical input; 0 = none yet
    private long _proactiveWorkTick; // 0 = never read
    private WorkSummary _proactiveWork = WorkSummary.Empty;
    private IReadOnlyList<ConnectorRef> _proactiveConnectors = [];

    /// <summary>UI thread, once at startup.</summary>
    private void WireProactive()
    {
        _physicalInput.UserActivity += _ => Volatile.Write(ref _proactiveActivityTick, Environment.TickCount64);
        _proactive = new ProactiveCoordinator(new ShellCardSink(() => _shell), new WindowsQuietSignal(Log), ProactiveCalendar,
            new TalkClientSuggestionSource(() => (ProviderFactory.CreateTalkClient(_settings, _secrets, Log), _settings.ResolveTalkModel())),
            ProactiveStore.Load(), state => ProactiveStore.Save(state), Log);
        _proactiveTimer.Tick += (_, _) =>
        {
            try { _proactive.Tick(ProactiveInputs()); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { Log("proactive_tick_error=" + ex.GetType().Name); }
        };
        _proactiveTimer.Start();
        _home.Disposed += (_, _) => _proactiveTimer.Dispose();
    }

    private ProactiveInputs ProactiveInputs()
    {
        var settings = _settings.Proactive ?? new();
        var now = DateTimeOffset.Now;
        var shellActive = _shell is { IsActive: true };
        if (shellActive && settings.AnyOn) RefreshProactiveWork(now);
        var activity = Volatile.Read(ref _proactiveActivityTick);
        DateTimeOffset? lastActivity = activity == 0 ? null : now - TimeSpan.FromMilliseconds(Environment.TickCount64 - activity);
        var busy = _talk.State != CompanionVoiceState.Idle || _talk.ConversationActive || _dictationActive || _taskCts is not null
            || _submissionCts is not null || _talk.Walkthrough.View.Active;
        var work = _proactiveWork;
        return new(settings, now, shellActive, busy, lastActivity, _settings.DisplayName,
            settings.IntegrationNudges ? ForegroundNudgeApp() : null, _proactiveConnectors, work.JobsNeedYou, work.JobsFinished,
            work.RoutinesToday, work.RecentJobTitles, work.RoutineTitles);
    }

    // SQLite reads at most once a minute; a failed read keeps the last good lists and waits for the next minute.
    private void RefreshProactiveWork(DateTimeOffset now)
    {
        var tick = Environment.TickCount64;
        if (_proactiveWorkTick != 0 && tick - _proactiveWorkTick < (long)ProactiveWorkRefresh.TotalMilliseconds) return;
        _proactiveWorkTick = tick;
        try
        {
            _proactiveWork = ProactiveHostInputs.Summarise(_work.Jobs(), _work.Routines(), now);
            _proactiveConnectors = _connectors.List().Select(c => new ConnectorRef(c.Key, c.DisplayName, c.Host)).ToList();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Log("proactive_work_error=" + ex.GetType().Name); }
    }

    // The title and process are read on this PC only and never stored; only the matched catalog app goes on.
    private NudgeApp? ForegroundNudgeApp()
    {
        var title = ForegroundWindowInfo.GetCompanionTitle();
        if (string.IsNullOrEmpty(title) || AgentDenyList.IsDenied(title, AgentDenyList.ParseExtraFragments(_settings.AgentDenyListExtra))) return null;
        string? process;
        try
        {
            using var p = Process.GetProcessById((int)ForegroundWindowInfo.ProcessIdOf(ForegroundWindowInfo.GetHandle()));
            process = p.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { process = null; }
        return NudgeCatalog.Match(process, title);
    }

    private ICalendarSource? ProactiveCalendar(string key) =>
        _connectors.List().FirstOrDefault(c => c.Key == key) is { } c && _connectors.Secret(c.Key) is { } secret
            ? new McpCalendarSource(c.Key, c.DisplayName, secret) : null;

    /// <summary>null when the card is not proactive; otherwise what <c>CardAction</c> returns (always false: the card goes).</summary>
    private bool? ProactiveCardAction(string cardId, string actionId)
    {
        if (_proactive?.CardAction(cardId, actionId) is not { } action) return null;
        switch (action.Kind)
        {
            case ProactiveActionKind.SubmitText when action.Value is { } text:
                _shell?.OpenNotch(NotchTab.Chat);
                _ = SubmitRequestAsync(text, agent: false, fromShell: true);
                break;
            case ProactiveActionKind.OpenUrl when action.Value is { } url:
                OpenUrl(url);
                break;
            case ProactiveActionKind.OpenIntegrations:
                _shell?.OpenNotch(NotchTab.Settings, "integrations");
                break;
        }
        return false;
    }

    private void ProactiveCardDismissed(string cardId) => _proactive?.CardDismissed(cardId);

    /// <summary>Puts the coordinator's cards in the notch; nothing without a shell.</summary>
    private sealed class ShellCardSink(Func<ShellController?> shell) : IProactiveCardSink
    {
        public void Show(NotchCard card) => shell()?.AddCard(card);
        public void Update(NotchCard card) => shell()?.UpdateCard(card);
        public void Remove(string cardId) => shell()?.RemoveCard(cardId);
    }
}
