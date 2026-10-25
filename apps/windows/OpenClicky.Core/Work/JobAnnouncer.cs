using OpenClicky.Core.Shell;

namespace OpenClicky.Core.Work;

public enum JobEvent { Started, Done, NeedsYou, Failed, Stopped }

/// <param name="Quiet">The user should not be interrupted by voice or cursor text (a call, a presentation, a full-screen app, quiet hours).</param>
/// <param name="Reason">Why, in a word or two; null when not quiet.</param>
public sealed record QuietState(bool Quiet, string? Reason)
{
    /// <summary><paramref name="notificationState"/> is <c>QUERY_USER_NOTIFICATION_STATE</c>: 2 busy, 3 full-screen Direct3D, 4 presentation,
    /// 5 accepts notifications, 6 quiet time. Any other value (not present, app full screen, unknown) is not quiet by itself.</summary>
    public static QuietState From(int notificationState, bool micInUseByOtherApp) => notificationState switch
    {
        2 => new(true, "Busy"),
        3 => new(true, "Full screen"),
        4 => new(true, "Presenting"),
        6 => new(true, "Quiet hours"),
        _ when micInUseByOtherApp => new(true, "On a call"),
        _ => new(false, null),
    };

    /// <summary>How long a microphone start with no stop can stand for a live call; older ones are an app that crashed with it open.</summary>
    public static readonly TimeSpan MicHolderMaxAge = TimeSpan.FromHours(12);

    /// <summary>Windows' consent store stamps a start and a stop time (FILETIME; 0 stop while open) per app. The mic is held open when the start
    /// is newer than the stop and not older than <see cref="MicHolderMaxAge"/>.</summary>
    public static bool MicHeldOpen(long startFileTime, long stopFileTime, DateTimeOffset now)
    {
        if (startFileTime <= 0 || startFileTime > DateTime.MaxValue.ToFileTimeUtc() || startFileTime <= stopFileTime) return false;
        return now - DateTimeOffset.FromFileTime(startFileTime) <= MicHolderMaxAge;
    }
}

/// <summary>What a job event says: a spoken line, a cursor bubble and a notch card. Quiet and a talk in progress mute the voice (and,
/// when quiet, the bubble); the card always stays.</summary>
public sealed record Announcement(string? Speak, string? Bubble, NotchCard? Card);

public static class JobAnnouncer
{
    public const string OpenAction = "open", CardIdPrefix = "job-update:";
    public const int MaxTitle = 80, CardMs = 10_000;

    public static string CardId(string jobId) => CardIdPrefix + jobId;

    /// <param name="title">Untrusted (model or user text): cut to <see cref="MaxTitle"/>. Never log the announcement.</param>
    /// <param name="talkBusy">A talk turn or a spoken reply is in progress: nothing else is spoken over it.</param>
    /// <param name="replyDelivered">The job was started by Talk and its end (Done or Failed) was already delivered as the reply: only the card is added.</param>
    /// <param name="scheduled">A routine started this run: its start is not announced (its end is).</param>
    public static Announcement For(JobEvent e, string title, string jobId, bool speakSetting, bool bubbleSetting, QuietState quiet, bool talkBusy,
        bool replyDelivered = false, bool scheduled = false)
    {
        ArgumentNullException.ThrowIfNull(quiet);
        title = Clean(title);
        var line = e switch
        {
            JobEvent.Started => $"On it: {title}.",
            JobEvent.Done => $"{title} is ready.",
            JobEvent.NeedsYou => $"{title} needs you.",
            JobEvent.Failed => $"{title} didn't finish.",
            _ => null,
        };
        var card = e switch
        {
            JobEvent.Done => Card(NotchCardKind.JobUpdate, "Ready to view", CardMs),
            JobEvent.Failed => Card(NotchCardKind.JobUpdate, "Didn't finish", CardMs),
            JobEvent.NeedsYou => Card(NotchCardKind.NeedsYou, "Needs you", null),
            _ => null,
        };
        if (replyDelivered && (e is JobEvent.Done or JobEvent.Failed) || scheduled && e == JobEvent.Started) line = null;
        return new(line is not null && speakSetting && !quiet.Quiet && !talkBusy ? line : null,
            line is not null && bubbleSetting && !quiet.Quiet ? line : null, card);

        NotchCard Card(NotchCardKind kind, string body, int? dismissMs) =>
            new(CardId(jobId), kind, title, body, [new(OpenAction, "Open", CardActionStyle.Primary)], dismissMs);
    }

    private static string Clean(string? title)
    {
        var t = (title ?? "").ReplaceLineEndings(" ").Trim();
        return t.Length == 0 ? "Your job" : t.Length <= MaxTitle ? t : t[..MaxTitle].TrimEnd();
    }
}
