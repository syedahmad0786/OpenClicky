namespace OpenClicky.Core.Proactive;

/// <summary>What the nudge tracker remembers across restarts: catalog app ids and times only, never titles.</summary>
public sealed class NudgeState
{
    public List<DateTimeOffset> Shown { get; set; } = [];
    public Dictionary<string, DateTimeOffset> SnoozedUntil { get; set; } = [];
    public List<string> Never { get; set; } = [];
}

/// <summary>
/// Decides when a foreground app has earned a connect nudge. The dwell credit and last sample live here and are not persisted.
/// Call <see cref="Observe"/> on every tick; it only decides, the caller shows the card.
/// </summary>
public sealed class NudgeTracker(NudgeState state)
{
    public static readonly TimeSpan Dwell = TimeSpan.FromSeconds(10), MaxCredit = TimeSpan.FromSeconds(30),
        Snooze = TimeSpan.FromHours(48), Window = TimeSpan.FromHours(24);
    public const int MaxPerWindow = 3;

    private string? _lastId;
    private DateTimeOffset _lastAt;
    private TimeSpan _credit;

    public NudgeApp? Observe(NudgeApp? foreground, DateTimeOffset now, bool canShow, Func<NudgeApp, bool> isConnected)
    {
        var sameApp = foreground is not null && foreground.Id == _lastId;
        var gap = now - _lastAt;
        _lastId = foreground?.Id;
        _lastAt = now;
        if (foreground is null) { _credit = TimeSpan.Zero; return null; }
        _credit = sameApp ? _credit + (gap < TimeSpan.Zero ? TimeSpan.Zero : gap > MaxCredit ? MaxCredit : gap) : TimeSpan.Zero;

        if (!canShow || _credit < Dwell || state.Never.Contains(foreground.Id)
            || (state.SnoozedUntil.TryGetValue(foreground.Id, out var until) && now < until)
            || isConnected(foreground)) return null;
        state.Shown.RemoveAll(t => now - t >= Window); // prune, so the list stays small
        if (state.Shown.Count >= MaxPerWindow) return null;

        state.Shown.Add(now);
        state.SnoozedUntil[foreground.Id] = now + Snooze;
        _credit = TimeSpan.Zero;
        return foreground;
    }

    public void Never(string appId)
    {
        if (!state.Never.Contains(appId)) state.Never.Add(appId);
    }
}
