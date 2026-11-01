namespace OpenClicky.Core;

/// <summary>Local activity metadata only; a suggestion is not permission to inspect.</summary>
public sealed class AppSwitchGuide
{
    private readonly Queue<(string Title, DateTimeOffset At)> _changes = new();
    private string? _current;
    private DateTimeOffset _since;
    private DateTimeOffset _nextOffer;

    public string? Observe(string? title, bool idle, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(title)) { _changes.Clear(); _current = null; return null; }
        if (title != _current) { _current = title; _since = now; _changes.Enqueue((title, now)); }
        while (_changes.TryPeek(out var first) && now - first.At > TimeSpan.FromSeconds(45)) _changes.Dequeue();
        if (!idle || now < _nextOffer || now - _since < TimeSpan.FromSeconds(2) || _changes.Count < 6
            || _changes.Select(item => item.Title).Distinct().Count() == _changes.Count) return null;
        _changes.Clear();
        _nextOffer = now.AddMinutes(5);
        return title;
    }

    public void Snooze(DateTimeOffset now) { _changes.Clear(); _nextOffer = now.AddMinutes(30); }
}
