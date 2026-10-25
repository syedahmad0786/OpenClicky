using System.Text.Json;
using OpenClicky.Core.Shell;

namespace OpenClicky.Core.Work;

/// <summary>
/// A schedule the user asked for by voice (E11). The model puts one fenced <see cref="FenceTag"/> block in its reply; the block is never
/// spoken or shown, only turned into a <see cref="RoutineRequest"/> that waits in memory for the user to press Schedule on
/// <see cref="ConfirmCard"/>. The block is untrusted: it is read with the same limits as a job result's routine, and an invalid one
/// is dropped. Only the first valid block of a reply counts.
/// </summary>
public static class RoutineRequests
{
    public const string FenceTag = "openclicky-routine", IdPrefix = "routine-confirm:", ScheduleAction = "schedule", DismissAction = "dismiss";
    public const int MaxBlockChars = 8_000, MaxTaskOnCard = 200;
    private const string Fence = "```" + FenceTag;

    /// <summary>Appended to the Talk system prompt.</summary>
    public const string TalkInstructions = "If the user asks you to do something on a schedule (for example \"every weekday at 8:30\" or \"every two hours\"), "
        + "do not do it now. Reply briefly that you can set it up, and add a fenced block tagged openclicky-routine holding one JSON object with "
        + "\"title\", \"task\" (what to do each time, written as an instruction) and either \"every_minutes\" or \"at\" (HH:MM, 24-hour) with optional "
        + "\"days\" (sun, mon, tue, wed, thu, fri, sat). The user confirms before anything is scheduled.";

    private static readonly JsonDocumentOptions Lenient = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 8 };

    /// <summary>The reply without any routine block, and the first valid request in it (null when none).</summary>
    public static (string Text, RoutineRequest? Request) Extract(string reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        RoutineRequest? request = null;
        return (Scan(reply, ref request), request);
    }

    /// <summary>Only the text, for a reply still streaming: a block being written, and a trailing start of the fence, are cut too.</summary>
    public static string Strip(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        RoutineRequest? ignored = null;
        return Scan(raw, ref ignored);
    }

    public static string CardId(string requestId) => IdPrefix + requestId;

    /// <summary>The request id inside a confirm card id, or null for any other card.</summary>
    public static string? RequestIdOf(string cardId) => cardId.StartsWith(IdPrefix, StringComparison.Ordinal) ? cardId[IdPrefix.Length..] : null;

    /// <summary>"Every day at 08:30", "Mon, Tue at 08:30", "Every 1 h": the words a stored routine with this schedule would show.</summary>
    public static string Cadence(RoutineRequest r) => RoutineSchedule.Describe(ToRoutine(r, "", DateTimeOffset.UnixEpoch));

    /// <summary>The model's text is untrusted and was capped when read; here it is also put on one line and the task is cut to
    /// <see cref="MaxTaskOnCard"/>. No auto-dismiss: the card waits for the user.</summary>
    public static NotchCard ConfirmCard(string requestId, RoutineRequest r) => new(CardId(requestId), NotchCardKind.NeedsYou, "Schedule this?",
        $"{OneLine(r.Title, WorkStore.MaxTitleLength)} · {Cadence(r)}\n{OneLine(r.Task, MaxTaskOnCard, ellipsis: true)}",
        [new(ScheduleAction, "Schedule", CardActionStyle.Primary), new(DismissAction, "Not now", CardActionStyle.Secondary)]);

    public static string ScheduledToast(RoutineRequest r) => $"Scheduled: {OneLine(r.Title, WorkStore.MaxTitleLength)} — {Cadence(r)}.";

    /// <summary>The routine this request would become, with the next run counted from <paramref name="now"/>.</summary>
    public static Routine ToRoutine(RoutineRequest r, string jobId, DateTimeOffset now) =>
        r.EveryMinutes is { } every ? new(Guid.NewGuid().ToString("N"), jobId, RoutineCadence.Interval, 0, 0, every, false, now)
        : new(Guid.NewGuid().ToString("N"), jobId, r.WeekdayMask == 127 ? RoutineCadence.Daily : RoutineCadence.Weekly,
            r.AtMinuteOfDay ?? 0, r.WeekdayMask, 0, false, now);

    private static string OneLine(string text, int max, bool ellipsis = false)
    {
        var line = text.ReplaceLineEndings(" ").Trim();
        return line.Length > max ? line[..max].TrimEnd() + (ellipsis ? "…" : "") : line;
    }

    private static RoutineRequest? Read(string json)
    {
        if (json.Length > MaxBlockChars) return null;
        try
        {
            using var document = JsonDocument.Parse(json, Lenient);
            return document.RootElement.ValueKind == JsonValueKind.Object ? JobResult.ReadRoutine(document.RootElement) : null;
        }
        catch (JsonException) { return null; }
    }

    // A block closes at a fence that starts a line, or at the first fence on its own opening line; an unclosed one runs to the end.
    private static string Scan(string raw, ref RoutineRequest? request)
    {
        var start = raw.IndexOf(Fence, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return CutPartialFence(raw, out var cut) ? cut : raw;
        var kept = new List<string>();
        var at = 0;
        while (start >= 0)
        {
            kept.Add(raw[at..start]);
            var body = start + Fence.Length;
            var lineEnd = raw.IndexOf('\n', body);
            var inline = raw.IndexOf("```", body, (lineEnd < 0 ? raw.Length : lineEnd) - body, StringComparison.Ordinal);
            var close = lineEnd < 0 ? -1 : raw.IndexOf("\n```", lineEnd, StringComparison.Ordinal);
            int content, end;
            if (inline >= 0) (content, end, at) = (body, inline, inline + 3);
            else if (close >= 0) (content, end, at) = (Math.Min(lineEnd + 1, close), close, close + 4);
            else { at = raw.Length; break; }
            request ??= Read(raw[content..end]);
            start = raw.IndexOf(Fence, at, StringComparison.OrdinalIgnoreCase);
        }
        var rest = raw[at..];
        kept.Add(CutPartialFence(rest, out var tail) ? tail : rest);
        return string.Join(" ", kept.Select(s => s.Trim()).Where(s => s.Length > 0));
    }

    /// <summary>A trailing "```o", "```openclicky-rou" … is the start of a block being written. A bare "```" is left alone.</summary>
    private static bool CutPartialFence(string text, out string cut)
    {
        var tail = text.LastIndexOf("```", StringComparison.Ordinal);
        var partial = tail >= 0 && text.Length - tail > 3 && Fence.StartsWith(text[tail..], StringComparison.OrdinalIgnoreCase);
        cut = partial ? text[..tail].TrimEnd() : text;
        return partial;
    }
}
