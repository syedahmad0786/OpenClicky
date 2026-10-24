using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenClicky.Core.Shell;

/// <summary>One saved job plus what the host knows about it right now. <see cref="Files"/> are full paths.</summary>
public sealed record ShellJobInput(WorkJob Job, IReadOnlyList<string> Files, IReadOnlyList<string> Bullets,
    string? LiveLine = null, bool HasDueRoutine = false);

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
    public string FallbackUserName { get; init; } = "";
    public string CurrentVersion { get; init; } = "0.0.0";
    public ShellNotch Notch { get; init; } = new(NotchTab.Home, false);
    public string? FeaturedJobId { get; init; }
    public IReadOnlySet<string> DismissedJobIds { get; init; } = new HashSet<string>();
    /// <summary>Rows not yet in the persisted history (the turn in progress), appended after it.</summary>
    public IReadOnlyList<ChatRow> LiveRows { get; init; } = [];
    /// <summary>Newest first; the builder keeps the first six.</summary>
    public IReadOnlyList<NotchCard> Cards { get; init; } = [];
    public ShellUpdate? Update { get; init; }
    public DocumentView? Document { get; init; }
}

/// <summary>
/// Maps app data to the shell contract and enforces its caps (jobs 80, chat rows 200, cards 6, bullets 4,
/// files 6, card actions 3, document 200,000 chars). Job states: running→working, completed→done,
/// failed→failed, stopped→stopped, ready→queued when a routine is due, otherwise new.
/// </summary>
public static class ShellStateBuilder
{
    public const int MaxJobs = 80, MaxChatRows = 200, MaxCards = 6, MaxBullets = 4, MaxFiles = 6, MaxCardActions = 3, MaxDocument = 200_000;
    public const string DefaultAccent = "#3380FF";
    private static readonly Regex Hex = new("^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
    private static readonly JobColour[] Colours = [JobColour.Red, JobColour.Blue, JobColour.Yellow, JobColour.Green];

    public static ShellState Build(ShellInputs inputs)
    {
        var settings = inputs.Settings;
        // Newest first (index 0 = most recently run or created), then trimmed.
        var cards = inputs.Jobs.OrderByDescending(j => j.Job.LastRunAt ?? j.Job.CreatedAt).Select(MapJob).ToList();
        var jobs = cards.Take(MaxJobs).ToList();
        var featured = Featured(inputs, cards);
        var rows = inputs.Chat.SelectMany(TurnRows).Concat(inputs.LiveRows).TakeLast(MaxChatRows).ToList();

        return new ShellState(
            ShellContract.Version,
            inputs.Theme,
            settings.AccentColor is { } accent && Hex.IsMatch(accent) ? accent.ToUpperInvariant() : DefaultAccent,
            inputs.ReducedMotion,
            new ShellUser(Cut(DisplayName(settings, inputs.FallbackUserName), 80)),
            new ShellHotkeys(Keys(settings.PushToTalk), [], [], []),
            new ShellStatus(inputs.Phase, cards.Count(j => j.State == JobState.Working), cards.Count(j => j.State == JobState.NeedsYou)),
            inputs.Notch,
            featured,
            jobs,
            new ShellChat(rows),
            inputs.Cards.Take(MaxCards).Select(c => c with { Actions = c.Actions.Take(MaxCardActions).ToList() }).ToList(),
            new ShellApps(inputs.Connectors.Take(64).Select(c => new ConnectedApp(Cut(c.Key, 160), Cut(c.DisplayName, 80))).ToList()),
            new ShellGeneral(settings.ShowInTaskbar, settings.ShowInScreenRecordings, settings.LaunchAtLogin, settings.Paused),
            inputs.Update ?? new ShellUpdate(Cut(inputs.CurrentVersion, 40), null, UpdateState.Idle, 0),
            inputs.Document is { } doc && doc.Content.Length > MaxDocument ? doc with { Content = doc.Content[..MaxDocument], Truncated = true } : inputs.Document);

        JobCard MapJob(ShellJobInput input)
        {
            var job = input.Job;
            var state = job.Status switch
            {
                WorkJobStates.Running => JobState.Working,
                AgentProgressStates.Completed => JobState.Done,
                AgentProgressStates.Failed => JobState.Failed,
                AgentProgressStates.Stopped => JobState.Stopped,
                WorkJobStates.Ready when input.HasDueRoutine => JobState.Queued,
                _ => JobState.New,
            };
            return new JobCard(
                Cut(job.Id, 160),
                Cut(string.IsNullOrWhiteSpace(job.Title) ? "Job" : job.Title.Trim(), 160),
                state,
                Colour(job.Id),
                string.IsNullOrWhiteSpace(job.Summary) ? null : Cut(job.Summary, 2_000),
                null,
                input.Bullets.Take(MaxBullets).Select(b => Cut(b, 400)).ToList(),
                input.Files.Take(MaxFiles).Select(path => new JobFile(Cut(Path.GetFileName(path), 260), path, FileKind(path))).ToList(),
                state == JobState.Working && !string.IsNullOrWhiteSpace(input.LiveLine) ? Cut(input.LiveLine, 400) : null,
                [],
                Iso(job.LastRunAt ?? job.CreatedAt));
        }
    }

    /// <summary>The job the host chose (job.open), else the most recently run job that produced files.</summary>
    private static JobCard? Featured(ShellInputs inputs, List<JobCard> cards)
    {
        if (inputs.FeaturedJobId is { } chosen && cards.FirstOrDefault(c => c.Id == chosen) is { } picked) return picked;
        return inputs.Jobs
            .Where(j => j.Job.LastRunAt is not null && j.Files.Count > 0 && !inputs.DismissedJobIds.Contains(j.Job.Id))
            .OrderByDescending(j => j.Job.LastRunAt)
            .Select(j => cards.First(c => c.Id == Cut(j.Job.Id, 160)))
            .FirstOrDefault();
    }

    private static IEnumerable<ChatRow> TurnRows(ConversationTurn turn)
    {
        var at = Iso(turn.CreatedAtUtc);
        if (!string.IsNullOrWhiteSpace(turn.User)) yield return new ChatRow($"turn-{turn.Id}-u", ChatRole.User, Cut(turn.User, 32_000), at);
        if (!string.IsNullOrWhiteSpace(turn.Assistant)) yield return new ChatRow($"turn-{turn.Id}-a", ChatRole.Assistant, Cut(turn.Assistant, 32_000), at);
    }

    public static string DisplayName(AppSettings settings, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(settings.DisplayName)) return settings.DisplayName.Trim();
        var local = settings.ChatGptAccountEmail?.Split('@')[0].Trim();
        if (!string.IsNullOrEmpty(local)) return char.ToUpper(local[0], CultureInfo.InvariantCulture) + local[1..];
        return fallback;
    }

    private static IReadOnlyList<string> Keys(HotkeyChord chord) =>
        chord.DisplayName.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(6).Select(k => Cut(k, 24)).ToList();

    /// <summary>Stable across runs (FNV-1a); string.GetHashCode is randomised per process.</summary>
    public static JobColour Colour(string id)
    {
        var hash = 2166136261u;
        foreach (var c in id) hash = unchecked((hash ^ c) * 16777619u);
        return Colours[hash % (uint)Colours.Length];
    }

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
