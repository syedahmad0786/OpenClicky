using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenClicky.Core.Shell;

namespace OpenClicky.Core.Work;

/// <summary>A schedule the model asked for. Only stored here; it becomes a routine only when the user presses Schedule (E11).
/// Either <see cref="EveryMinutes"/> or <see cref="AtMinuteOfDay"/> is set; the weekday mask uses bit (int)DayOfWeek (0 for intervals).</summary>
public sealed record RoutineRequest(string Title, string Task, int? EveryMinutes, int? AtMinuteOfDay, int WeekdayMask);

/// <summary>One step the user must do themselves. <see cref="Page"/> is http(s) only and opens only when the user asks (E8).</summary>
public sealed record UserStep(string Step, string? Page, string? DoneWhenUrlContains, DateTimeOffset AskedAt);

/// <summary>
/// A job run's structured result, read from the last fenced <see cref="FenceTag"/> block of the model's reply.
/// The reply is untrusted: every field is optional, capped, and files must resolve to existing files inside the job folder.
/// <see cref="Plan"/> is never read from the block; the host fills it from the run's own checklist.
/// </summary>
public sealed record JobResult(string Reply, string? Title, string? Summary, IReadOnlyList<string> Points, JobStat? Stat,
    IReadOnlyList<string> Next, IReadOnlyList<string> Files, RoutineRequest? Routine, UserStep? YourTurn, IReadOnlyList<string> Learned)
{
    public const string FenceTag = "openclicky-result";
    public const int MaxPath = 4_096;

    public const string Instructions = "When you finish, end your reply with a fenced block tagged openclicky-result holding one JSON object. "
        + "Use \"title\" for a short name of what you made or did, \"summary\" for one sentence, \"points\" for up to four key findings, "
        + "\"stat\" for one headline number as {\"value\",\"label\"} if there is one, \"next\" for up to three suggested next steps, "
        + "and \"files\" for the paths of files you saved under output/. If the user must do one step themselves (sign in, approve, pay), "
        + "add \"your_turn\" with \"step\", the \"page\" to open and \"done_when_url_contains\" text that appears in the address once they are done. "
        + "If the user asked for something to repeat, add \"routine\" with \"title\", \"task\" and either \"every_minutes\" or \"at\" (HH:MM) plus optional \"days\". "
        + "Add \"learned\" for short, reusable lessons about how to do things on this PC. Leave out anything that does not apply.";

    // The tag is case-insensitive and may be followed by more words on the opening line ("```openclicky-result json").
    private static readonly Regex Block = new(@"^[ \t]*```" + FenceTag + @"(?:[ \t][^\r\n]*)?\r?\n(?<json>.*?)\r?\n[ \t]*```[ \t]*\r?$",
        RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly JsonDocumentOptions Lenient = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
    private static readonly string[] Days = ["sun", "mon", "tue", "wed", "thu", "fri", "sat"];

    public IReadOnlyList<PlanStep> Plan { get; init; } = [];

    private static JobResult Empty(string reply) => new(reply, null, null, [], null, [], [], null, null, []);

    /// <summary>Reads the last result block. Without a block, or when it is not one JSON object, the reply is returned unchanged.
    /// Otherwise every result block is removed from <see cref="Reply"/>. An empty <paramref name="jobFolder"/> accepts no files.</summary>
    public static JobResult Parse(string reply, string jobFolder)
    {
        MatchCollection blocks;
        try
        {
            blocks = Block.Matches(reply);
            if (blocks.Count == 0) return Empty(reply);
        }
        catch (RegexMatchTimeoutException) { return Empty(reply); }
        JsonDocument document;
        try { document = JsonDocument.Parse(blocks[^1].Groups["json"].Value, Lenient); }
        catch (JsonException) { return Empty(reply); }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Empty(reply);
            var stripped = reply;
            for (var i = blocks.Count - 1; i >= 0; i--) stripped = stripped.Remove(blocks[i].Index, blocks[i].Length);
            // The headline number may come as a JSON number ("value":12) or a string ("value":"12%").
            var stat = Get(root, "stat") is { ValueKind: JsonValueKind.Object } s
                && (Get(s, "value") is { ValueKind: JsonValueKind.Number } number ? Cut(number.GetRawText(), 40) : Text(Get(s, "value"), 40)) is { } value
                ? new JobStat(value, Text(Get(s, "label"), 80) ?? "") : null;
            return new JobResult(stripped.Trim(), Text(Get(root, "title"), 120), Text(Get(root, "summary"), 300),
                Texts(Get(root, "points"), 4, 200), stat, Texts(Get(root, "next"), 3, 160),
                Texts(Get(root, "files"), 50, int.MaxValue).Select(f => SafeFile(jobFolder, f)).OfType<string>()
                    .Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToList(),
                ReadRoutine(Get(root, "routine")), ReadStep(Get(root, "your_turn")), Texts(Get(root, "learned"), 5, 240));
        }
    }

    /// <summary>The full path of an existing file inside <paramref name="folder"/>, or null. Rejects "..", other roots, alternate
    /// data streams, and any symlink or junction between the folder and the file.</summary>
    public static string? SafeFile(string folder, string path)
    {
        if (folder.Length == 0 || path.Length is 0 or > MaxPath) return null;
        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
            var full = Path.GetFullPath(Path.Combine(root, path)); // a rooted path replaces root, then fails the prefix check
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;
            if (full.IndexOf(':', 2) >= 0 || !File.Exists(full)) return null;
            for (var p = full; p.Length > root.Length; p = Path.GetDirectoryName(p)!)
                if ((File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) return null;
            return full;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException) { return null; }
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>What a finished run stores for its card: its own result, else (a stopped or failed run) the previous one, so the last
    /// good points, stat, next steps and files stay; with the run's own checklist when it had one. The reply is left out: the card never
    /// reads it (runs.md and the chat keep it), and it can be large. Null: nothing to store.</summary>
    public static JobResult? ForStore(JobResult? produced, JobResult? previous, IReadOnlyList<PlanStep> plan)
    {
        var kept = produced ?? previous;
        if (plan.Count > 0) kept = (kept ?? Empty("")) with { Plan = plan };
        return kept is null ? null : kept with { Reply = "" };
    }

    /// <summary>Reads a stored result; null when there is none or it cannot be read.</summary>
    public static JobResult? FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<JobResult>(json, Json) is { } r
                ? r with { Reply = r.Reply ?? "", Points = r.Points ?? [], Next = r.Next ?? [], Files = r.Files ?? [], Learned = r.Learned ?? [], Plan = r.Plan ?? [] }
                : null;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException) { return null; }
    }

    internal static RoutineRequest? ReadRoutine(JsonElement? element)
    {
        if (element is not { ValueKind: JsonValueKind.Object } routine
            || Text(Get(routine, "title"), WorkStore.MaxTitleLength) is not { } title
            || Text(Get(routine, "task"), WorkStore.MaxPromptLength) is not { } task) return null;
        if (Get(routine, "every_minutes") is { } every)
            return every.ValueKind == JsonValueKind.Number && every.TryGetInt32(out var minutes)
                && minutes is >= RoutineSchedule.MinIntervalMinutes and <= RoutineSchedule.MaxIntervalMinutes
                ? new RoutineRequest(title, task, minutes, null, 0) : null;
        if (Get(routine, "at") is not { ValueKind: JsonValueKind.String } at
            || !TimeOnly.TryParseExact(at.GetString(), ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) return null;
        var mask = 0;
        if (Get(routine, "days") is { } days)
        {
            if (days.ValueKind != JsonValueKind.Array) return null;
            foreach (var day in days.EnumerateArray())
            {
                var index = day.ValueKind == JsonValueKind.String ? Array.IndexOf(Days, day.GetString()!.Trim().ToLowerInvariant()) : -1;
                if (index < 0) return null;
                mask |= 1 << index;
            }
        }
        return new RoutineRequest(title, task, null, time.Hour * 60 + time.Minute, mask == 0 ? 127 : mask);
    }

    private static UserStep? ReadStep(JsonElement? element)
    {
        if (element is not { ValueKind: JsonValueKind.Object } step || Text(Get(step, "step"), 600) is not { } text) return null;
        // A link is never cut (a shorter URL points somewhere else): one over 2048 characters is dropped.
        // Stored in canonical form (Uri.AbsoluteUri), so what is shown is what opens.
        var page = Text(Get(step, "page"), int.MaxValue) is { Length: <= 2_048 } link
            && Uri.TryCreate(link, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            && uri.AbsoluteUri is { Length: <= 2_048 } canonical ? canonical : null;
        return new UserStep(text, page, Text(Get(step, "done_when_url_contains"), 200), DateTimeOffset.UtcNow);
    }

    private static JsonElement? Get(JsonElement obj, string name)
    {
        foreach (var property in obj.EnumerateObject())
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind != JsonValueKind.Null) return property.Value;
        return null;
    }

    private static string? Text(JsonElement? element, int max) =>
        element is { ValueKind: JsonValueKind.String } e && e.GetString()!.Trim() is { Length: > 0 } text ? text[..Math.Min(text.Length, max)] : null;

    private static string Cut(string value, int max) => value[..Math.Min(value.Length, max)];

    private static IReadOnlyList<string> Texts(JsonElement? element, int count, int max) =>
        element is { ValueKind: JsonValueKind.Array } array
            ? array.EnumerateArray().Select(e => Text(e, max)).OfType<string>().Take(count).ToList() : [];
}
