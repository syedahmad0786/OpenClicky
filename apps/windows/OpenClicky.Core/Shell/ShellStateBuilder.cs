using System.Globalization;
using System.Text.RegularExpressions;

using OpenClicky.Core.Settings;
using OpenClicky.Core.Work;

namespace OpenClicky.Core.Shell;

/// <summary>One saved job plus what the host knows about it right now. <see cref="Files"/> are full paths.
/// <see cref="LiveLine"/> shows while the job works or waits (queued); <see cref="Plan"/> is the running job's live checklist; a job that is not working shows the plan stored with its result.</summary>
public sealed record ShellJobInput(WorkJob Job, IReadOnlyList<string> Files, IReadOnlyList<string> Bullets,
    string? LiveLine = null, bool HasDueRoutine = false, IReadOnlyList<PlanStep>? Plan = null);

/// <summary>Everything <see cref="ShellStateBuilder"/> needs. Host-owned data in the constructor, controller-owned in init props.</summary>
public sealed record ShellInputs(
    AppSettings Settings,
    IReadOnlyList<ShellJobInput> Jobs,
    IReadOnlyList<ConversationTurn> Chat,
    IReadOnlyList<ConnectorInfo> Connectors,
    ShellPhase Phase,
    DateTimeOffset Now)
{
    public ShellTheme Theme { get; init; } = ShellTheme.Dark;
    public bool ReducedMotion { get; init; }
    /// <summary>Last resort, one word (the OS user name).</summary>
    public string FallbackUserName { get; init; } = "";
    /// <summary>The Windows account's display name (may be several words or empty); its first word is the greeting name.</summary>
    public string AccountDisplayName { get; init; } = "";
    public string CurrentVersion { get; init; } = "0.0.0";
    public ShellNotch Notch { get; init; } = new(NotchTab.Home, false, ShellContract.DefaultSection);
    public string? FeaturedJobId { get; init; }
    public IReadOnlySet<string> DismissedJobIds { get; init; } = new HashSet<string>();
    /// <summary>Saved drawings (any order). Each becomes an event row "Drew {title}" in chat at its time, carrying the drawing id.</summary>
    public IReadOnlyList<(string Id, string Title, DateTimeOffset CreatedAt)> Drawings { get; init; } = [];
    /// <summary>Rows not yet in the persisted history (the turn in progress), appended after it.</summary>
    public IReadOnlyList<ChatRow> LiveRows { get; init; } = [];
    /// <summary>Newest first; the builder keeps the first six.</summary>
    public IReadOnlyList<NotchCard> Cards { get; init; } = [];
    public ShellUpdate? Update { get; init; }
    public DocumentView? Document { get; init; }
    /// <summary>An explicit settings page to show; otherwise built from <see cref="Settings"/> and <see cref="SettingsRuntime"/>. Either way it is clamped to the contract caps.</summary>
    public SettingsView? SettingsPage { get; init; }
    /// <summary>Machine facts for the settings page; used only when <see cref="SettingsPage"/> is not supplied.</summary>
    public SettingsRuntime SettingsRuntime { get; init; } = SettingsRuntime.Empty;
}

/// <summary>
/// Maps app data to the shell contract and enforces its caps (jobs 80, chat rows 200, cards 6, bullets 4, next steps 3,
/// files 6, card actions 3, document 200,000 chars). Job states: running→working, queued→queued, needs-you→needs you,
/// completed→done, failed→failed, stopped→stopped, ready→queued when a routine is due, otherwise new.
/// </summary>
public static class ShellStateBuilder
{
    public const int MaxJobs = 80, MaxChatRows = 200, MaxCards = 6, MaxBullets = 4, MaxNext = 3, MaxFiles = 6, MaxCardActions = 3, MaxDocument = 200_000;
    /// <summary>Per chat row in state (longer text ends in "…"), and the whole serialized state (oldest chat rows dropped first).</summary>
    public const int MaxRowText = 8_000, MaxStateChars = 1_000_000;
    /// <summary>Longest file path the page schema accepts; longer paths are dropped, never cut (the open allow-list needs them exact).</summary>
    public const int MaxPath = 4_096;
    public const string DefaultAccent = "#3380FF";
    internal static bool IsHex(string? value) => value is not null && Hex.IsMatch(value);
    private static readonly Regex Hex = new("^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
    private static readonly JobColour[] Colours = [JobColour.Red, JobColour.Blue, JobColour.Yellow, JobColour.Green];

    public static ShellState Build(ShellInputs inputs)
    {
        var settings = inputs.Settings;
        // Each job's stored result is read once per push (the card, its files and the featured pick all use it).
        var results = new Dictionary<ShellJobInput, JobResult?>(ReferenceEqualityComparer.Instance);
        JobResult? Result(ShellJobInput input)
        {
            if (!results.TryGetValue(input, out var result)) results[input] = result = JobResult.FromJson(input.Job.ResultJson);
            return result;
        }
        // Newest first (index 0 = most recently run or created), then trimmed.
        var cards = inputs.Jobs.Where(j => !string.IsNullOrEmpty(j.Job.Id)).OrderByDescending(j => j.Job.LastRunAt ?? j.Job.CreatedAt).Select(MapJob).ToList();
        var jobs = cards.Take(MaxJobs).ToList();
        var featured = Featured(inputs, cards, Result);
        var rows = MergeDrawings(inputs.Chat.SelectMany(t => TurnRows(t).Select(r => (At: t.CreatedAtUtc, Row: r))), inputs.Drawings).Concat(inputs.LiveRows).TakeLast(MaxChatRows)
            .Select(r => r.Text.Length <= MaxRowText ? r : r with { Text = r.Text[..(MaxRowText - 1)] + "…" }).ToList();

        var state = new ShellState(
            ShellContract.Version,
            inputs.Theme,
            IsHex(settings.AccentColor) ? settings.AccentColor.ToUpperInvariant() : DefaultAccent,
            inputs.ReducedMotion,
            new ShellUser(Cut(DisplayName(settings, inputs.AccountDisplayName, inputs.FallbackUserName), 80)),
            Hotkeys(Input.ShortcutBindings.From(settings)),
            new ShellStatus(inputs.Phase, cards.Count(j => j.State == JobState.Working), cards.Count(j => j.State == JobState.NeedsYou)),
            inputs.Notch,
            featured,
            jobs,
            new ShellChat(rows),
            inputs.Cards.Take(MaxCards).Select(c => c with { Actions = c.Actions.Take(MaxCardActions).ToList() }).ToList(),
            new ShellApps(inputs.Connectors.Where(c => !string.IsNullOrEmpty(c.Key)).Take(64).Select(c => new ConnectedApp(Cut(c.Key, 160), Cut(c.DisplayName, 80))).ToList()),
            new ShellGeneral(settings.ShowInTaskbar, settings.ShowInScreenRecordings, settings.LaunchAtLogin, settings.Paused),
            BoundUpdate(inputs.Update) ?? new ShellUpdate(Cut(inputs.CurrentVersion, 40), null, UpdateState.Idle, 0),
            inputs.Document is { } pathDoc && pathDoc.Path.Length > MaxPath ? null
                : inputs.Document is { } doc && doc.Content.Length > MaxDocument ? doc with { Content = doc.Content[..MaxDocument], Truncated = true } : inputs.Document,
            inputs.SettingsPage is { } page ? ShellSettingsBuilder.Clamp(page) : ShellSettingsBuilder.Build(settings, inputs.SettingsRuntime)); // Build already clamps
        return WithinBudget(state);

        JobCard MapJob(ShellJobInput input)
        {
            var job = input.Job;
            var state = job.Status switch
            {
                WorkJobStates.Running => JobState.Working,
                WorkJobStates.Queued => JobState.Queued,
                WorkJobStates.NeedsYou => JobState.NeedsYou,
                WorkJobStates.Completed => JobState.Done,
                WorkJobStates.Failed => JobState.Failed,
                WorkJobStates.Stopped => JobState.Stopped,
                WorkJobStates.Ready when input.HasDueRoutine => JobState.Queued,
                _ => JobState.New,
            };
            // The last run's stored result; a run in progress shows its own live steps instead of the last run's points.
            var stored = Result(input);
            var result = state == JobState.Working ? null : stored;
            return new JobCard(
                Cut(job.Id, 160),
                Cut(string.IsNullOrWhiteSpace(job.Title) ? "Job" : job.Title.Trim(), 160),
                state,
                Colour(job),
                string.IsNullOrWhiteSpace(job.Summary) ? null : Cut(job.Summary, 2_000),
                result?.Stat is { } stat ? new JobStat(Cut(stat.Value, 40), Cut(stat.Label, 80)) : null,
                (result is { Points.Count: > 0 } ? result.Points : input.Bullets).Take(MaxBullets).Select(b => Cut(b, 400)).ToList(),
                (result?.Next ?? []).Take(MaxNext).Select(n => Cut(n, 160)).ToList(),
                UsableFiles(JobFiles(input, stored)).Select(path => new JobFile(Cut(Path.GetFileName(path), 260), path, FileKind(path))).ToList(),
                (state is JobState.Working or JobState.Queued) && !string.IsNullOrWhiteSpace(input.LiveLine) ? Cut(input.LiveLine, 400) : null,
                (state == JobState.Working ? input.Plan ?? [] : result?.Plan ?? []).Take(JobProgress.MaxPlan)
                    .Select(step => new PlanStep(Cut(step.Text, 400), step.Done)).ToList(),
                Iso(job.LastRunAt ?? job.CreatedAt));
        }
    }

    /// <summary>Drops the oldest chat rows until the serialized state fits <see cref="MaxStateChars"/>.</summary>
    private static ShellState WithinBudget(ShellState state)
    {
        var rows = state.Chat.Rows.ToList();
        for (var excess = ShellContract.Serialize(state).Length - MaxStateChars; excess > 0 && rows.Count > 0;
             excess = ShellContract.Serialize(state).Length - MaxStateChars)
        {
            var drop = 0;
            for (long freed = 0; drop < rows.Count && freed < excess; drop++) freed += rows[drop].Text.Length + 100;
            rows.RemoveRange(0, drop);
            state = state with { Chat = new ShellChat(rows.ToList()) };
        }
        return state;
    }

    /// <summary>The files the job's stored result named (checked inside the job folder when it was parsed), else today's output listing.</summary>
    private static IReadOnlyList<string> JobFiles(ShellJobInput input, JobResult? stored) =>
        stored is { Files.Count: > 0 } result ? result.Files : input.Files;

    private static IEnumerable<string> UsableFiles(IReadOnlyList<string> files) =>
        files.Where(path => path.Length is > 0 and <= MaxPath).Take(MaxFiles);

    private static ShellUpdate? BoundUpdate(ShellUpdate? update) => update is null ? null : update with
    {
        Current = Cut(update.Current ?? "", 40),
        Available = update.Available is null ? null : Cut(update.Available, 40),
        Progress = double.IsFinite(update.Progress) ? Math.Clamp(update.Progress, 0, 1) : 0,
    };

    /// <summary>The job the host chose (job.open), else the most recently run job that produced files.</summary>
    private static JobCard? Featured(ShellInputs inputs, List<JobCard> cards, Func<ShellJobInput, JobResult?> result)
    {
        if (inputs.FeaturedJobId is { } chosen && cards.FirstOrDefault(c => c.Id == chosen) is { } picked) return picked;
        return inputs.Jobs
            .Where(j => !string.IsNullOrEmpty(j.Job.Id) && j.Job.LastRunAt is not null && UsableFiles(JobFiles(j, result(j))).Any() && !inputs.DismissedJobIds.Contains(j.Job.Id))
            .OrderByDescending(j => j.Job.LastRunAt)
            .Select(j => cards.First(c => c.Id == Cut(j.Job.Id, 160)))
            .FirstOrDefault();
    }

    /// <summary>Drawing rows join the turn rows in time order (a stable sort: a turn row comes before a drawing row of the same instant).</summary>
    private static IEnumerable<ChatRow> MergeDrawings(IEnumerable<(DateTimeOffset At, ChatRow Row)> turns, IReadOnlyList<(string Id, string Title, DateTimeOffset CreatedAt)> drawings) =>
        turns.Concat(drawings.Where(d => d.Id is { Length: > 0 }).Select(d => (At: d.CreatedAt,
                Row: new ChatRow("drawing-" + Cut(d.Id, 100), ChatRole.Event, Cut("Drew " + d.Title, 200), Iso(d.CreatedAt), DrawingId: Cut(d.Id, 160)))))
            .OrderBy(x => x.At).Select(x => x.Row);

    private static IEnumerable<ChatRow> TurnRows(ConversationTurn turn)
    {
        var at = Iso(turn.CreatedAtUtc);
        if (!string.IsNullOrWhiteSpace(turn.User)) yield return new ChatRow($"turn-{turn.Id}-u", ChatRole.User, Cut(turn.User, 32_000), at);
        if (!string.IsNullOrWhiteSpace(turn.Assistant)) yield return new ChatRow($"turn-{turn.Id}-a", ChatRole.Assistant, Cut(turn.Assistant, 32_000), at);
    }

    /// <summary>One word: the saved name, else the Windows account name's first word, else the email's leading letters, else the OS user.</summary>
    public static string DisplayName(AppSettings settings, string accountName, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(settings.DisplayName)) return settings.DisplayName.Trim();
        if (accountName.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() is { } first) return first;
        var local = settings.ChatGptAccountEmail?.Split('@')[0].Trim() ?? "";
        var letters = new string(local.TakeWhile(char.IsLetter).ToArray());
        if (letters.Length > 0) return char.ToUpper(letters[0], CultureInfo.InvariantCulture) + letters[1..].ToLowerInvariant();
        return fallback;
    }

    private static ShellHotkeys Hotkeys(Input.ShortcutBindings b) => new(Keys(b.Talk), Keys(b.Text), Keys(b.Dictate), Keys(b.HandsFree));

    private static IReadOnlyList<string> Keys(HotkeyBinding binding) => binding.Keys.Take(6).Select(k => Cut(k, 24)).ToList();

    internal static IReadOnlyList<string> Keys(HotkeyChord chord) =>
        chord.DisplayName.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(6).Select(k => Cut(k, 24)).ToList();

    /// <summary>The job's stored colour; a missing or unknown name falls back to the colour its id hash gave before colours were stored.</summary>
    public static JobColour Colour(WorkJob job) =>
        Colours[JobColours.IndexOf(job.Colour) is var i and >= 0 ? i : JobColours.IndexOf(JobColours.FromId(job.Id))];

    public static JobFileKind FileKind(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".md" or ".txt" or ".doc" or ".docx" or ".rtf" or ".odt" or ".html" or ".htm" => JobFileKind.Doc,
        ".pdf" => JobFileKind.Pdf,
        ".csv" or ".tsv" or ".xls" or ".xlsx" or ".ods" => JobFileKind.Sheet,
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" or ".svg" => JobFileKind.Image,
        ".mp3" or ".wav" or ".m4a" or ".ogg" or ".flac" => JobFileKind.Audio,
        ".mp4" or ".mov" or ".webm" or ".mkv" or ".avi" => JobFileKind.Video,
        ".url" => JobFileKind.Link,
        _ => JobFileKind.Other,
    };

    public static string Iso(DateTimeOffset at) => at.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static string Cut(string value, int max) => value.Length <= max ? value : value[..max];
}
