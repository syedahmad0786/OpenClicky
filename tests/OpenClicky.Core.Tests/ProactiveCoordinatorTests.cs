using System.Text.Json;
using OpenClicky.Core.Proactive;
using OpenClicky.Core.Shell;

namespace OpenClicky.Core.Tests;

public class ProactiveCoordinatorTests
{
    private static readonly TimeSpan Zone = TimeSpan.FromHours(2); // explicit offset: never the machine's time zone
    private static DateTimeOffset At(int day, int h, int m, double s = 0) => new DateTimeOffset(2026, 10, day, h, m, 0, Zone).AddSeconds(s);
    private static readonly DateTimeOffset Mon = At(26, 0, 0);
    private static NudgeApp App(string id) => NudgeCatalog.Apps.Single(a => a.Id == id);

    private const string TwoSuggestions = "{\"suggestions\":[{\"title\":\"Tidy downloads\",\"description\":\"Frees space.\",\"prompt\":\"Tidy my downloads folder\"},"
        + "{\"title\":\"Review notes\",\"description\":\"Keeps you current.\",\"prompt\":\"Summarise my notes\"}]}";

    private static UpcomingEvent Event(string id, DateTimeOffset start, string title = "Standup", string? meetingUrl = "https://meet.example.com/abc") =>
        new(id, title, start, start.AddMinutes(30), false, meetingUrl, null, null);

    private sealed class Sink : IProactiveCardSink
    {
        public readonly List<string> Calls = [];
        public readonly Dictionary<string, NotchCard> Cards = [];
        public void Show(NotchCard card) { Calls.Add("show:" + card.Id); Cards[card.Id] = card; }
        public void Update(NotchCard card) { Calls.Add("update:" + card.Id); Cards[card.Id] = card; }
        public void Remove(string cardId) { Calls.Add("remove:" + cardId); Cards.Remove(cardId); }
    }

    private sealed class Quiet : IQuietSignal
    {
        public string? Reason;
        public string? QuietReason() => Reason;
    }

    private sealed class Calendar : ICalendarSource
    {
        public readonly List<(DateTimeOffset From, DateTimeOffset To)> Calls = [];
        public readonly List<TaskCompletionSource<IReadOnlyList<UpcomingEvent>>> Pending = [];
        public IReadOnlyList<UpcomingEvent>? Events; // null: the task stays pending until the test completes it
        public Task<IReadOnlyList<UpcomingEvent>> EventsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
        {
            Calls.Add((from, to));
            if (Events is not null) return Task.FromResult(Events);
            var t = new TaskCompletionSource<IReadOnlyList<UpcomingEvent>>();
            Pending.Add(t);
            return t.Task;
        }
    }

    private sealed class Source : ISuggestionSource
    {
        public readonly List<(string System, string User)> Calls = [];
        public Func<Task<string?>> Reply = () => Task.FromResult<string?>(TwoSuggestions);
        public Task<string?> CompleteAsync(string systemPrompt, string userMessage, CancellationToken ct)
        {
            Calls.Add((systemPrompt, userMessage));
            return Reply();
        }
    }

    /// <summary>One coordinator over an in-memory "file"; <see cref="Restart"/> builds a new one from the last save.</summary>
    private sealed class Harness
    {
        public readonly Sink Sink = new();
        public readonly Quiet Quiet = new();
        public Calendar Calendar = new();
        public readonly Source Source = new();
        public readonly List<string> Logs = [];
        public readonly List<string?> CalendarKeys = [];
        public int Saves;
        public string Saved = "{}";
        public ProactiveState State = new();
        public ProactiveCoordinator C;
        public ProactiveSettings Settings = new();
        public NudgeApp? Foreground;
        public IReadOnlyList<ConnectorRef> Connectors = [];
        public bool Busy, ShellActive = true;

        public Harness(ProactiveSettings? settings = null)
        {
            Settings = settings ?? new();
            C = Create();
        }

        private ProactiveCoordinator Create() => new(Sink, Quiet, key => { CalendarKeys.Add(key); return Calendar; }, Source, State,
            s => { Saves++; Saved = JsonSerializer.Serialize(s); }, Logs.Add);

        public void Restart(Calendar? calendar = null)
        {
            State = JsonSerializer.Deserialize<ProactiveState>(Saved)!;
            if (calendar is not null) Calendar = calendar;
            Sink.Cards.Clear();
            C = Create();
        }

        public void Tick(DateTimeOffset now, DateTimeOffset? activity = default, bool active = true) =>
            C.Tick(new(Settings, now, ShellActive, Busy, active ? activity ?? now : null, "Ahmad", Foreground, Connectors,
                0, 0, [], ["Fix login bug"], ["Daily digest"]));

        /// <summary>Ticks every 2.5 s from <paramref name="from"/> up to and including <paramref name="to"/>.</summary>
        public void Run(DateTimeOffset from, DateTimeOffset to, Action<DateTimeOffset>? after = null)
        {
            for (var t = from; t <= to; t = t.AddSeconds(2.5)) { Tick(t); after?.Invoke(t); }
        }

        public string? SuggestionCard() => Sink.Cards.Keys.SingleOrDefault(k => k.StartsWith(ProactiveCards.SuggestionPrefix, StringComparison.Ordinal));
    }

    [Fact]
    public void AllOffShowsNothingAndCallsNothing()
    {
        foreach (var settings in new[] { new ProactiveSettings(), new ProactiveSettings { CalendarConnector = "cal" } })
        {
            var h = new Harness(settings) { Foreground = App("notion") };
            for (var t = Mon; t < Mon.AddDays(1); t = t.AddSeconds(30)) h.Tick(t);
            Assert.Empty(h.Sink.Calls);
            Assert.Empty(h.CalendarKeys);
            Assert.Empty(h.Calendar.Calls);
            Assert.Empty(h.Source.Calls);
            Assert.Equal(0, h.Saves);
            Assert.Null(h.State.BriefDay);
        }
    }

    [Fact]
    public void BriefOncePerDayAcrossRestart()
    {
        var h = new Harness(new() { MorningBrief = true });
        h.Tick(At(26, 6, 59));
        h.Tick(At(26, 8, 0), active: false); // in the window but no recent input
        Assert.Empty(h.Sink.Calls);

        h.Tick(At(26, 8, 0, 2.5));
        Assert.Equal(["show:" + ProactiveCards.BriefId], h.Sink.Calls);
        var card = h.Sink.Cards[ProactiveCards.BriefId];
        Assert.Equal("Good morning, Ahmad", card.Title);
        Assert.Equal("Nothing on your plate yet.", card.Body);
        Assert.Equal("2026-10-26", h.State.BriefDay);
        Assert.Equal(1, h.Saves);
        Assert.Empty(h.CalendarKeys); // no connector: nothing to wait for

        var plan = h.C.CardAction(ProactiveCards.BriefId, ProactiveCards.PlanAction);
        Assert.Equal(new ProactiveAction(ProactiveActionKind.SubmitText, MorningBrief.PlanPrompt(["Nothing on your plate yet."])), plan);
        h.Tick(At(26, 8, 0, 5));
        Assert.DoesNotContain(ProactiveCards.BriefId, h.Sink.Cards.Keys);

        h.Restart();
        h.Run(At(26, 8, 5), At(26, 8, 6));
        Assert.Empty(h.Sink.Cards);

        h.Tick(At(27, 8, 0));
        Assert.Contains(ProactiveCards.BriefId, h.Sink.Cards.Keys);
        Assert.Equal("2026-10-27", h.State.BriefDay);

        h.Tick(At(27, 12, 0)); // leaving the window drops a brief nobody acted on
        Assert.Empty(h.Sink.Cards);
    }

    [Fact]
    public void BriefWaitsForCalendar()
    {
        var h = new Harness(new() { MorningBrief = true, CalendarConnector = "cal" });
        h.Tick(At(26, 8, 0));
        Assert.Equal(["cal"], h.CalendarKeys);
        Assert.Equal([(At(26, 5, 0), At(27, 0, 0))], h.Calendar.Calls);
        h.Run(At(26, 8, 0, 2.5), At(26, 8, 1));
        Assert.Empty(h.Sink.Calls);
        Assert.Single(h.Calendar.Calls); // one fetch at a time

        h.Calendar.Pending[0].SetResult([Event("e1", At(26, 10, 0))]);
        h.Tick(At(26, 8, 1, 2.5));
        Assert.Equal("1 meeting today: Standup at 10:00.", h.Sink.Cards[ProactiveCards.BriefId].Body);

        // a failed first fetch also lets the brief through, with no meeting line, and logs only the exception type
        h = new Harness(new() { MorningBrief = true, CalendarConnector = "cal" });
        h.Tick(At(26, 8, 0));
        h.Calendar.Pending[0].SetException(new HttpRequestException("server said: secret-token Standup"));
        h.Tick(At(26, 8, 0, 2.5));
        Assert.Equal("Nothing on your plate yet.", h.Sink.Cards[ProactiveCards.BriefId].Body);
        Assert.Equal(["proactive_calendar_error=HttpRequestException"], h.Logs);
        h.Run(At(26, 8, 0, 5), At(26, 8, 1));
        Assert.Single(h.Calendar.Calls); // a failed fetch is retried after 60 s
        h.Tick(At(26, 8, 1, 2.5));
        Assert.Equal(2, h.Calendar.Calls.Count);

        // an unknown connector key is no calendar: the brief does not wait
        h = new Harness(new() { MorningBrief = true, CalendarConnector = "gone" });
        var keys = new List<string>();
        h.C = new ProactiveCoordinator(h.Sink, h.Quiet, k => { keys.Add(k); return null; }, h.Source, h.State, _ => { }, h.Logs.Add);
        h.Tick(At(26, 8, 0));
        Assert.Equal(["gone"], keys);
        Assert.Contains(ProactiveCards.BriefId, h.Sink.Cards.Keys);
    }

    [Fact]
    public void CountdownShowsUpdatesAndEnds()
    {
        var h = new Harness(new() { MeetingCountdown = true, CalendarConnector = "cal" });
        h.Calendar.Events = [Event("e1", At(26, 10, 0))];
        var id = ProactiveCards.MeetingCardId("e1");
        var log = new List<(DateTimeOffset At, string Call, string? Body)>();
        h.Run(At(26, 9, 55), At(26, 10, 3), t =>
        {
            foreach (var call in h.Sink.Calls) log.Add((t, call, h.Sink.Cards.GetValueOrDefault(id)?.Body));
            h.Sink.Calls.Clear();
        });

        Assert.Equal((At(26, 9, 58, 45), "show:" + id, "Starts in 1:15"), log[0]);
        Assert.Equal(new[] { At(26, 9, 59), At(26, 9, 59, 15), At(26, 9, 59, 30), At(26, 9, 59, 45), At(26, 10, 0), At(26, 10, 1) },
            log.Where(l => l.Call.StartsWith("update:", StringComparison.Ordinal)).Select(l => l.At));
        Assert.Equal(["Starts in 1:00", "Starts in 0:45", "Starts in 0:30", "Starts in 0:15", "Starting now", "Started 1 min ago"],
            log.Where(l => l.Call.StartsWith("update:", StringComparison.Ordinal)).Select(l => l.Body));
        Assert.Equal((At(26, 10, 2), "remove:" + id), (log[^1].At, log[^1].Call));
        Assert.Equal(8, log.Count);
        Assert.True(h.Calendar.Calls.Count is >= 2 and <= 3); // refreshed every 5 min, not every tick
    }

    [Fact]
    public void DismissedMeetingStaysDismissed()
    {
        var events = new[] { Event("e1", At(26, 10, 0)), Event("e2", At(26, 10, 30), "Review", "http://insecure.example.com/x") };
        var h = new Harness(new() { MeetingCountdown = true, CalendarConnector = "cal" });
        h.Calendar.Events = events;
        string e1 = ProactiveCards.MeetingCardId("e1"), e2 = ProactiveCards.MeetingCardId("e2");

        h.Run(At(26, 9, 58), At(26, 9, 58, 45));
        Assert.Contains(e1, h.Sink.Cards.Keys);
        h.C.CardDismissed(e1);
        Assert.Equal(At(26, 10, 0), h.State.DismissedMeetings["e1"]);
        h.Sink.Calls.Clear();
        h.Run(At(26, 9, 58, 47.5), At(26, 10, 1));
        Assert.Empty(h.Sink.Calls);

        h.Restart(new Calendar { Events = events });
        h.Run(At(26, 10, 1), At(26, 10, 1, 30));
        Assert.Empty(h.Sink.Calls);

        // the next meeting's link is not https, so Join/Open give nothing to open, but the click still dismisses it
        h.Run(At(26, 10, 28, 45), At(26, 10, 28, 47.5));
        Assert.Contains(e2, h.Sink.Cards.Keys);
        Assert.Empty(h.Sink.Cards[e2].Actions);
        Assert.Equal(new ProactiveAction(ProactiveActionKind.None), h.C.CardAction(e2, ProactiveCards.JoinAction));
        Assert.True(h.State.DismissedMeetings.ContainsKey("e2"));

        // a safe link opens the normalised URL; a dismissal is forgotten a day after the meeting
        h = new Harness(new() { MeetingCountdown = true, CalendarConnector = "cal" });
        h.Calendar.Events = [Event("e3", At(26, 10, 0), meetingUrl: "https://MEET.example.com/abc")];
        h.Run(At(26, 9, 58, 40), At(26, 9, 58, 45));
        Assert.Equal(new ProactiveAction(ProactiveActionKind.OpenUrl, "https://meet.example.com/abc"),
            h.C.CardAction(ProactiveCards.MeetingCardId("e3"), ProactiveCards.JoinAction));
        h.Tick(At(27, 10, 0, 2.5));
        Assert.Empty(h.State.DismissedMeetings);
    }

    [Fact]
    public void QuietRemovesAndRestores()
    {
        var h = new Harness(new() { MorningBrief = true, Suggestions = true, MeetingCountdown = true, IntegrationNudges = true, CalendarConnector = "cal" })
        {
            Foreground = App("notion"),
        };
        h.Calendar.Events = [Event("e1", At(26, 10, 0))];
        var meeting = ProactiveCards.MeetingCardId("e1");
        h.Run(At(26, 9, 58, 30), At(26, 9, 58, 45));
        var suggestion = h.SuggestionCard();
        Assert.NotNull(suggestion);
        Assert.Equal(new[] { ProactiveCards.BriefId, meeting, ProactiveCards.NudgePrefix + "notion", suggestion }.Order(), h.Sink.Cards.Keys.Order());

        h.Quiet.Reason = "On a call";
        h.Run(At(26, 9, 58, 47.5), At(26, 9, 58, 52.5));
        Assert.Empty(h.Sink.Cards);
        Assert.Single(h.Logs, "proactive_quiet=On a call");

        h.Quiet.Reason = null;
        h.Tick(At(26, 9, 58, 55));
        Assert.Equal(new[] { ProactiveCards.BriefId, meeting, suggestion }.Order(), h.Sink.Cards.Keys.Order());

        // turning one feature off removes only its card
        h.Settings.Suggestions = false;
        h.Tick(At(26, 9, 58, 57.5));
        Assert.Equal(new[] { ProactiveCards.BriefId, meeting }.Order(), h.Sink.Cards.Keys.Order());

        // the classic window (no notch shell) shows nothing
        h.ShellActive = false;
        h.Tick(At(26, 9, 59));
        Assert.Empty(h.Sink.Cards);
    }

    [Fact]
    public void BusyHoldsNewCardsButNotCountdown()
    {
        var h = new Harness(new() { MorningBrief = true, Suggestions = true, MeetingCountdown = true, IntegrationNudges = true, CalendarConnector = "cal" })
        {
            Foreground = App("notion"),
            Busy = true,
        };
        h.Calendar.Events = [Event("e1", At(26, 10, 0))];
        h.Run(At(26, 9, 58), At(26, 9, 59));
        Assert.Equal([ProactiveCards.MeetingCardId("e1")], h.Sink.Cards.Keys);
        Assert.Empty(h.Source.Calls);

        h.Busy = false;
        h.Run(At(26, 9, 59, 2.5), At(26, 9, 59, 5));
        Assert.Equal(4, h.Sink.Cards.Count); // brief, countdown, nudge (dwell credited while busy) and a suggestion

        h.Busy = true;
        h.Sink.Calls.Clear();
        h.Tick(At(26, 9, 59, 7.5));
        Assert.DoesNotContain(h.Sink.Calls, c => c.StartsWith("remove:", StringComparison.Ordinal)); // shown cards stay
    }

    [Fact]
    public void SuggestionsOneAtATimeAndActions()
    {
        var h = new Harness(new() { Suggestions = true });
        h.Tick(At(26, 8, 59));
        Assert.Empty(h.Source.Calls);
        h.Tick(At(26, 9, 0));
        var call = Assert.Single(h.Source.Calls);
        Assert.Equal(SuggestionPrompt.SystemPrompt, call.System);
        Assert.Contains("- Fix login bug", call.User);

        h.Tick(At(26, 9, 0, 2.5));
        Assert.Equal(2, h.State.Suggestions.Pending.Count);
        var first = h.SuggestionCard()!;
        Assert.Equal("Tidy downloads", h.Sink.Cards[first].Title);

        Assert.Equal(new ProactiveAction(ProactiveActionKind.SubmitText, "Tidy my downloads folder"), h.C.CardAction(first, ProactiveCards.DoItAction));
        Assert.Single(h.State.Suggestions.Pending);
        Assert.Single(JsonSerializer.Deserialize<ProactiveState>(h.Saved)!.Suggestions.Pending);

        h.Tick(At(26, 9, 0, 5));
        var second = h.SuggestionCard()!;
        Assert.Equal("Review notes", h.Sink.Cards[second].Title);

        Assert.Equal(new ProactiveAction(ProactiveActionKind.None), h.C.CardAction(second, ProactiveCards.NeverAction));
        Assert.Equal(["Review notes"], h.State.Suggestions.Declined);
        Assert.Empty(h.State.Suggestions.Pending);
        h.Run(At(26, 9, 0, 7.5), At(26, 9, 1));
        Assert.Null(h.SuggestionCard());
        Assert.Single(h.Source.Calls); // once a day
    }

    [Fact]
    public void SuggestionFailureBacksOff()
    {
        var h = new Harness(new() { Suggestions = true });
        h.Source.Reply = () => Task.FromException<string?>(new HttpRequestException("provider said: secret detail"));
        h.Tick(At(26, 9, 0));
        h.Tick(At(26, 9, 0, 2.5));
        Assert.Equal(1, h.State.Suggestions.Failures);
        Assert.Equal(At(26, 9, 5, 2.5), h.State.Suggestions.NextAttempt);
        Assert.Equal(["proactive_suggestions_error=HttpRequestException"], h.Logs);

        h.Run(At(26, 9, 0, 5), At(26, 9, 5));
        Assert.Single(h.Source.Calls);
        h.Tick(At(26, 9, 5, 2.5));
        Assert.Equal(2, h.Source.Calls.Count);

        // a reply that never comes counts as a failure after 60 s, by the caller's clock
        h.Source.Reply = () => new TaskCompletionSource<string?>().Task;
        h.Tick(At(26, 9, 5, 5)); // reads the second failure; next attempt 15 min later
        Assert.Equal(2, h.State.Suggestions.Failures);
        h.Tick(At(26, 9, 20, 5));
        Assert.Equal(3, h.Source.Calls.Count);
        h.Tick(At(26, 9, 21, 2.5));
        Assert.Equal(2, h.State.Suggestions.Failures);
        h.Tick(At(26, 9, 21, 5));
        Assert.Equal(3, h.State.Suggestions.Failures);
        Assert.Equal("proactive_suggestions_error=Timeout", h.Logs[^1]);
        Assert.DoesNotContain(h.Logs, l => l.Contains("secret", StringComparison.Ordinal));
        Assert.Null(h.SuggestionCard());
    }

    [Fact]
    public void NoProviderSkipsTheDay()
    {
        var h = new Harness(new() { Suggestions = true });
        h.Source.Reply = () => Task.FromResult<string?>(null);
        h.Tick(At(26, 9, 0));
        h.Tick(At(26, 9, 0, 2.5));
        Assert.Equal(["proactive_suggestions_no_provider"], h.Logs);
        Assert.Equal("2026-10-26", h.State.Suggestions.Day);
        for (var t = At(26, 9, 5); t < At(26, 20, 0); t = t.AddMinutes(5)) h.Tick(t);
        Assert.Single(h.Source.Calls);
        Assert.Null(h.SuggestionCard());

        h.Tick(At(27, 9, 0));
        Assert.Equal(2, h.Source.Calls.Count);
    }

    [Fact]
    public void NudgeAfterDwellAndConnectOpensIntegrations()
    {
        var h = new Harness(new() { IntegrationNudges = true }) { Foreground = App("notion") };
        var id = ProactiveCards.NudgePrefix + "notion";
        h.Run(At(26, 14, 0), At(26, 14, 0, 7.5));
        Assert.Empty(h.Sink.Cards);
        h.Tick(At(26, 14, 0, 10));
        Assert.Equal(["show:" + id], h.Sink.Calls);
        Assert.Equal(ProactiveCards.NudgeMs, h.Sink.Cards[id].AutoDismissMs);
        Assert.Single(JsonSerializer.Deserialize<ProactiveState>(h.Saved)!.Nudges.Shown);

        Assert.Equal(new ProactiveAction(ProactiveActionKind.OpenIntegrations), h.C.CardAction(id, ProactiveCards.ConnectAction));
        h.Tick(At(26, 14, 0, 12.5));
        Assert.DoesNotContain(id, h.Sink.Cards.Keys);

        // a nudge nobody touches is forgotten after 18 s (the shell hides it), so another app can earn one; "never" is kept
        h.Foreground = App("slack");
        h.Run(At(26, 14, 0, 15), At(26, 14, 0, 25));
        var slack = ProactiveCards.NudgePrefix + "slack";
        Assert.Contains(slack, h.Sink.Cards.Keys);
        h.Foreground = App("linear");
        h.Run(At(26, 14, 0, 27.5), At(26, 14, 0, 40));
        Assert.DoesNotContain(h.Sink.Calls, c => c == "show:" + ProactiveCards.NudgePrefix + "linear"); // the slack nudge is still up
        h.Run(At(26, 14, 0, 42.5), At(26, 14, 0, 45));
        Assert.Contains("show:" + ProactiveCards.NudgePrefix + "linear", h.Sink.Calls);
        Assert.Equal(new ProactiveAction(ProactiveActionKind.None), h.C.CardAction(ProactiveCards.NudgePrefix + "linear", ProactiveCards.NeverAction));
        Assert.Equal(["linear"], JsonSerializer.Deserialize<ProactiveState>(h.Saved)!.Nudges.Never);

        // an app that is already connected is never nudged
        h = new Harness(new() { IntegrationNudges = true }) { Foreground = App("notion"), Connectors = [new("n", "Notion", "mcp.notion.com")] };
        h.Run(At(26, 14, 0), At(26, 14, 1));
        Assert.Empty(h.Sink.Calls);
    }

    [Fact]
    public void NonProactiveCardReturnsNull()
    {
        var h = new Harness(new() { MorningBrief = true });
        Assert.Null(h.C.CardAction("dictation-activity", "stop"));
        h.C.CardDismissed("dictation-activity");
        Assert.Equal(0, h.Saves);
        Assert.Equal(new ProactiveAction(ProactiveActionKind.None), h.C.CardAction(ProactiveCards.SuggestionPrefix + "gone", ProactiveCards.DoItAction));
        Assert.Equal(new ProactiveAction(ProactiveActionKind.None), h.C.CardAction("proactive-unknown", "x"));
        Assert.Empty(h.Sink.Cards);
    }
}
