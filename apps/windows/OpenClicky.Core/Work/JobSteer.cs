namespace OpenClicky.Core.Work;

/// <summary>A steer for a job that has not started yet: it is added to the ask the job will start with.</summary>
public static class JobSteer
{
    /// <summary>The longest a queued job's ask may grow to.</summary>
    public const int MaxAsk = 8_000;

    /// <summary><paramref name="current"/> (the follow-up, else the job's request) with <paramref name="text"/> on a new line;
    /// null when that would be longer than <see cref="MaxAsk"/>.</summary>
    public static string? Append(string? current, string text)
    {
        var ask = string.IsNullOrWhiteSpace(current) ? text : current.Trim() + "\n" + text;
        return ask.Length > MaxAsk ? null : ask;
    }
}
