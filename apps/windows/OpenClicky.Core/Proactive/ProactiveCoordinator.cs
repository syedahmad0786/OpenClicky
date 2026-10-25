using OpenClicky.Core.Shell;

namespace OpenClicky.Core.Proactive;

/// <summary>Where the coordinator puts its cards (the notch shell in the app, a recorder in tests).</summary>
public interface IProactiveCardSink
{
    void Show(NotchCard card);
    void Update(NotchCard card);
    void Remove(string cardId);
}

/// <summary>One tick's view of the app. <see cref="Now"/> is local time with its offset.</summary>
public sealed record ProactiveInputs(ProactiveSettings Settings, DateTimeOffset Now, bool ShellActive, bool Busy, DateTimeOffset? LastUserActivity,
    string? DisplayName, NudgeApp? ForegroundApp, IReadOnlyList<ConnectorRef> Connectors, int JobsNeedYou, int JobsFinished,
    IReadOnlyList<RoutineSlot> RoutinesToday, IReadOnlyList<string> RecentJobTitles, IReadOnlyList<string> RoutineTitles);

public enum ProactiveActionKind { None, SubmitText, OpenUrl, OpenIntegrations }

/// <summary>What the host does after the user clicked a proactive card action: send text, open a safe URL, open Integrations, or nothing.</summary>
public sealed record ProactiveAction(ProactiveActionKind Kind, string? Value = null);

/// <summary>
/// Decides which proactive cards are up. Single-threaded: <see cref="Tick"/>, <see cref="CardAction"/> and
/// <see cref="CardDismissed"/> all run on the caller's thread; calendar fetches and suggestion runs are started as tasks
/// and only read (via IsCompleted) on a later tick, so there are no locks. It only shows and removes cards; anything
/// that acts is returned from <see cref="CardAction"/>, the user's click. Logs carry ids, counts and exception type names only.
/// </summary>
public sealed class ProactiveCoordinator(IProactiveCardSink sink, IQuietSignal quiet, Func<string, ICalendarSource?> calendarFor,
    ISuggestionSource suggestions, ProactiveState state, Action<ProactiveState> save, Action<string> log)
{
    public static readonly TimeSpan CalendarRefresh = TimeSpan.FromMinutes(5), CalendarRetry = TimeSpan.FromSeconds(60),
        CalendarAround = TimeSpan.FromHours(3), CalendarTimeout = TimeSpan.FromSeconds(60), NudgeShownFor = TimeSpan.FromMilliseconds(ProactiveCards.NudgeMs), KeepDismissed = TimeSpan.FromDays(1);

    private readonly NudgeTracker _tracker = new(state.Nudges);
    private readonly Dictionary<string, NotchCard> _shown = [];

    // Calendar: the cache and the one fetch in flight, for the connector key they belong to.
    private string? _calendarKey;
    private bool _noCalendar, _fetchedOnce;
    private Task<IReadOnlyList<UpcomingEvent>>? _fetch;
    private CancellationTokenSource? _fetchCts;
    private IReadOnlyList<UpcomingEvent>? _events;
    private DateTimeOffset? _lastFetchOk, _retryFetchAt;
    private DateTimeOffset _fetchAt;

    // Wanted cards (kept through quiet; the nudge is not).
    private NotchCard? _brief, _meeting, _nudge;
    private IReadOnlyList<string>? _briefLines;
    private string? _suggestionId;
    private DateTimeOffset _nudgeAt;

    private Task<string?>? _generation;
    private CancellationTokenSource? _generationCts;
    private DateTimeOffset _generationAt;
    private string? _quietLogged;

    public void Tick(ProactiveInputs inputs)
    {
        var (settings, now) = (inputs.Settings, inputs.Now);

        // 1. Prune; forget a nudge the shell has hidden by now.
        var before = state.Suggestions.Pending.Count + state.DismissedMeetings.Count;
        SuggestionSchedule.PruneExpired(state.Suggestions, now);
        foreach (var (id, start) in state.DismissedMeetings.ToList())
            if (now - start > KeepDismissed) state.DismissedMeetings.Remove(id);
        if (state.Suggestions.Pending.Count + state.DismissedMeetings.Count != before) Save();
        if (_nudge is not null && now - _nudgeAt >= NudgeShownFor) { _shown.Remove(_nudge.Id); _nudge = null; }

        // A feature turned off stops wanting only its own card; a brief nobody acted on goes when the window ends.
        if (!settings.MorningBrief || (_brief is not null && !MorningBrief.InWindow(now))) { _brief = null; _briefLines = null; }
        if (!settings.MeetingCountdown) _meeting = null;
        if (!settings.Suggestions) _suggestionId = null;
        if (!settings.IntegrationNudges) _nudge = null;

        // 2. No notch shell or nothing on: nothing shown.
        if (!inputs.ShellActive || !settings.AnyOn) { Reconcile(); return; }

        // 3. Calendar (fetches run even while quiet).
        Calendar(settings, now);

        // 4. Quiet: take everything down, keep what is wanted (but not the nudge).
        if (quiet.QuietReason() is { } reason)
        {
            if (reason != _quietLogged) log("proactive_quiet=" + reason);
            _quietLogged = reason;
            _nudge = null;
            Reconcile();
            return;
        }
        _quietLogged = null;

        // 5. Countdown, even while busy.
        _meeting = settings.MeetingCountdown && MeetingCountdown.Current(_events ?? [], now, state.DismissedMeetings) is { } e
            ? ProactiveCards.Meeting(ProactiveCards.MeetingCardId(e.Id), e, now) : null;

        // 6–9. Busy holds new cards; nudge dwell is still credited.
        if (!inputs.Busy)
        {
            if (settings.MorningBrief) Brief(inputs);
            if (settings.Suggestions) Suggestions(inputs);
        }
        if (settings.IntegrationNudges
            && _tracker.Observe(inputs.ForegroundApp, now, !inputs.Busy && _nudge is null, a => NudgeCatalog.IsConnected(a, inputs.Connectors)) is { } app)
        {
            _nudge = ProactiveCards.Nudge(app);
            _nudgeAt = now;
            Save();
        }
        Reconcile(_brief, _meeting, SuggestionCard(), _nudge);
    }

    /// <summary>The user clicked an action. null when the card is not proactive (the host handles it).</summary>
    public ProactiveAction? CardAction(string cardId, string actionId)
    {
        if (!ProactiveCards.IsProactive(cardId)) return null;
        var action = Drop(cardId, actionId);
        sink.Remove(cardId);
        return action;
    }

    /// <summary>The user closed a card: a brief or suggestion is dropped, a meeting dismissed, a nudge just goes.</summary>
    public void CardDismissed(string cardId)
    {
        if (ProactiveCards.IsProactive(cardId)) Drop(cardId, null);
    }

    // Stops wanting the card, applies what the click (or dismissal, actionId null) means, and saves.
    private ProactiveAction Drop(string cardId, string? actionId)
    {
        var action = new ProactiveAction(ProactiveActionKind.None);
        _shown.Remove(cardId);
        if (cardId == ProactiveCards.BriefId)
        {
            if (actionId == ProactiveCards.PlanAction && _briefLines is not null)
                action = new(ProactiveActionKind.SubmitText, MorningBrief.PlanPrompt(_briefLines));
            _brief = null;
            _briefLines = null;
        }
        else if (cardId.StartsWith(ProactiveCards.MeetingPrefix, StringComparison.Ordinal))
        {
            if (_events?.FirstOrDefault(e => ProactiveCards.MeetingCardId(e.Id) == cardId) is { } e)
            {
                if (actionId is ProactiveCards.JoinAction or ProactiveCards.OpenAction
                    && (MeetingCountdown.SafeUrl(e.MeetingUrl) ?? MeetingCountdown.SafeUrl(e.HtmlLink)) is { } url)
                    action = new(ProactiveActionKind.OpenUrl, url);
                state.DismissedMeetings[e.Id] = e.StartsAt;
            }
            _meeting = null;
        }
        else if (cardId.StartsWith(ProactiveCards.SuggestionPrefix, StringComparison.Ordinal))
        {
            var id = cardId[ProactiveCards.SuggestionPrefix.Length..];
            if (state.Suggestions.Pending.Find(p => p.Id == id) is { } p)
            {
                state.Suggestions.Pending.Remove(p);
                if (actionId == ProactiveCards.DoItAction) action = new(ProactiveActionKind.SubmitText, p.Prompt);
                else if (actionId == ProactiveCards.NeverAction) SuggestionSchedule.Decline(state.Suggestions, p.Title);
            }
            if (_suggestionId == id) _suggestionId = null;
        }
        else if (cardId.StartsWith(ProactiveCards.NudgePrefix, StringComparison.Ordinal))
        {
            var appId = cardId[ProactiveCards.NudgePrefix.Length..];
            if (actionId == ProactiveCards.ConnectAction) action = new(ProactiveActionKind.OpenIntegrations);
            else if (actionId == ProactiveCards.NeverAction && NudgeCatalog.Apps.Any(a => a.Id == appId)) _tracker.Never(appId);
            if (_nudge?.Id == cardId) _nudge = null;
        }
        Save();
        return action;
    }

    private void Calendar(ProactiveSettings settings, DateTimeOffset now)
    {
        var key = (settings.MeetingCountdown || settings.MorningBrief) && !string.IsNullOrWhiteSpace(settings.CalendarConnector) ? settings.CalendarConnector : null;
        if (key != _calendarKey)
        {
            _fetchCts?.Cancel();
            _fetchCts?.Dispose();
            (_calendarKey, _fetch, _fetchCts, _events, _lastFetchOk, _retryFetchAt, _noCalendar, _fetchedOnce) = (key, null, null, null, null, null, false, false);
        }
        if (key is null) return;

        // The connector's body read has no timeout of its own, so a fetch still running after 60 s by the caller's clock is abandoned.
        if (_fetch is { } done && (done.IsCompleted || now - _fetchAt >= CalendarTimeout))
        {
            var timedOut = !done.IsCompleted;
            _fetch = null;
            if (timedOut) _fetchCts?.Cancel();
            _fetchCts?.Dispose();
            _fetchCts = null;
            if (timedOut) CalendarFailed(now, "Timeout");
            else if (done.IsCompletedSuccessfully) (_events, _lastFetchOk, _retryFetchAt) = (done.Result, now, null);
            else CalendarFailed(now, ErrorName(done));
            _fetchedOnce = true;
        }
        if (_fetch is not null || now < _retryFetchAt || now - _lastFetchOk < CalendarRefresh) return;

        try
        {
            if (calendarFor(key) is not { } source)
            {
                // A key the host does not know is no calendar: the brief does not wait; look again later.
                (_noCalendar, _events, _retryFetchAt) = (true, null, now + CalendarRetry);
                return;
            }
            _noCalendar = false;
            var endOfDay = new DateTimeOffset(now.Date.AddDays(1), now.Offset);
            _fetchCts = new CancellationTokenSource(CalendarTimeout);
            _fetchAt = now;
            _fetch = source.EventsAsync(now - CalendarAround, endOfDay > now + CalendarAround ? endOfDay : now + CalendarAround, _fetchCts.Token);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _fetch = null;
            _fetchCts?.Dispose();
            _fetchCts = null;
            CalendarFailed(now, ex.GetType().Name);
            _fetchedOnce = true;
        }
    }

    private void CalendarFailed(DateTimeOffset now, string error)
    {
        log("proactive_calendar_error=" + error);
        _retryFetchAt = now + CalendarRetry;
    }

    private void Brief(ProactiveInputs inputs)
    {
        var now = inputs.Now;
        if (_brief is not null || !MorningBrief.IsDue(now, state.BriefDay, inputs.LastUserActivity)) return;
        var hasCalendar = _calendarKey is not null && !_noCalendar;
        if (hasCalendar && !_fetchedOnce) return;
        var today = hasCalendar
            ? _events?.Where(e => MorningBrief.DayKey(e.StartsAt.ToOffset(now.Offset)) == MorningBrief.DayKey(now)).ToList()
            : null;
        _briefLines = MorningBrief.Lines(now, today, inputs.JobsNeedYou, inputs.JobsFinished, inputs.RoutinesToday);
        _brief = ProactiveCards.Brief(MorningBrief.Title(inputs.DisplayName), _briefLines);
        state.BriefDay = MorningBrief.DayKey(now);
        Save();
    }

    private void Suggestions(ProactiveInputs inputs)
    {
        var (now, s) = (inputs.Now, state.Suggestions);
        if (_generation is { } g && (g.IsCompleted || now - _generationAt >= SuggestionSchedule.Timeout))
        {
            _generation = null;
            _generationCts?.Cancel();
            if (g.IsCompleted) _generationCts?.Dispose(); // an abandoned run may still hold the token
            _generationCts = null;
            if (g.IsCompletedSuccessfully)
            {
                if (g.Result is null) log("proactive_suggestions_no_provider");
                SuggestionSchedule.Succeeded(s, now, SuggestionPrompt.Parse(g.Result, now, () => Guid.NewGuid().ToString("N")));
            }
            else
            {
                log("proactive_suggestions_error=" + (g.IsCompleted ? ErrorName(g) : "Timeout"));
                SuggestionSchedule.Failed(s, now);
            }
            Save();
        }

        if (_generation is null && SuggestionSchedule.IsDue(s, now))
        {
            var message = SuggestionPrompt.BuildUserMessage(now, inputs.RecentJobTitles, inputs.RoutineTitles,
                inputs.Connectors.Select(c => c.Name).ToList(), s.Declined);
            _generationCts = new CancellationTokenSource(SuggestionSchedule.Timeout);
            _generationAt = now;
            try { _generation = suggestions.CompleteAsync(SuggestionPrompt.SystemPrompt, message, _generationCts.Token); }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _generationCts.Dispose();
                _generationCts = null;
                log("proactive_suggestions_error=" + ex.GetType().Name);
                SuggestionSchedule.Failed(s, now);
                Save();
            }
        }

        if (_suggestionId is null || !s.Pending.Exists(p => p.Id == _suggestionId)) _suggestionId = s.Pending.FirstOrDefault()?.Id;
    }

    private NotchCard? SuggestionCard() =>
        _suggestionId is not null && state.Suggestions.Pending.Find(p => p.Id == _suggestionId) is { } p ? ProactiveCards.Suggestion(p) : null;

    // Shows wanted cards that are not up, updates changed ones, removes the rest.
    private void Reconcile(params NotchCard?[] wanted)
    {
        var want = wanted.OfType<NotchCard>().ToList();
        foreach (var id in _shown.Keys.Where(id => want.All(c => c.Id != id)).ToList())
        {
            _shown.Remove(id);
            sink.Remove(id);
        }
        foreach (var card in want)
        {
            if (!_shown.TryGetValue(card.Id, out var old)) sink.Show(card);
            else if (old.Kind != card.Kind || old.Title != card.Title || old.Body != card.Body || old.AutoDismissMs != card.AutoDismissMs
                     || !old.Actions.SequenceEqual(card.Actions)) sink.Update(card);
            else continue;
            _shown[card.Id] = card;
        }
    }

    private void Save()
    {
        try { save(state); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { log("proactive_save_error=" + ex.GetType().Name); }
    }

    private static string ErrorName(Task t) => t.IsCanceled ? "Canceled" : t.Exception?.InnerException?.GetType().Name ?? "Unknown";
}
