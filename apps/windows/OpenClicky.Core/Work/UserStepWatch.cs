using System.Text.Json;

namespace OpenClicky.Core.Work;

public enum UserStepStatus { Waiting, Done, TimedOut }

/// <summary>What to do with a stored step when a needs-you job is picked up again (after a restart, or when a start of it was undone).</summary>
public enum UserStepResume { Watch, TimedOut, Unreadable }

/// <summary>
/// Decides whether the step a job is waiting on the user for is done: the browser address now contains the step's
/// "done when" text, or the user said so (which the caller handles), or six hours passed. The address is untrusted and private:
/// it is only compared here, never stored or logged.
/// An address match counts only once this watch has read an address that did not match (a page that already matches when the step
/// is asked, or when the watch resumes, proves nothing), never for done-when text under 4 characters or a bare "http"/"https"/"www",
/// and at most <see cref="AutoContinueBudget.PerDay"/> times a day per job (<paramref name="budget"/>); past that only "I'm done" ends it.
/// Not thread-safe: one watch loop calls <see cref="Check"/>.
/// </summary>
public sealed class UserStepWatch(UserStep step, DateTimeOffset startedAt, AutoContinueBudget? budget = null, string jobId = "")
{
    private static readonly HashSet<string> GenericTokens = new(StringComparer.OrdinalIgnoreCase) { "http", "https", "www" };
    private readonly string? _doneWhen = Meaningful(step.DoneWhenUrlContains);
    private bool _sawNonMatch;

    public static readonly TimeSpan Poll = TimeSpan.FromSeconds(2.5), Limit = TimeSpan.FromHours(6);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public const string TimedOutSummary = "Waited six hours for your step; stopped.";

    public UserStep Step { get; } = step;

    /// <summary>Watch a readable step that still has time left (its six hours count from <see cref="UserStep.AskedAt"/>); a readable one past
    /// the limit is stopped with <see cref="TimedOutSummary"/> without being shown; one that cannot be read is stopped, for nothing could end it.</summary>
    public static (UserStepResume Outcome, UserStep? Step) Resume(string? userStepJson, DateTimeOffset now) =>
        FromJson(userStepJson) is not { } step ? (UserStepResume.Unreadable, null)
        : now - step.AskedAt >= Limit ? (UserStepResume.TimedOut, step)
        : (UserStepResume.Watch, step);

    /// <summary>A step without usable "done when" text never completes by itself; only the user's "I'm done" (or the limit) ends it.</summary>
    public UserStepStatus Check(DateTimeOffset now, string? currentAddress)
    {
        if (currentAddress is not null && _doneWhen is { } text)
        {
            if (!currentAddress.Contains(text, StringComparison.OrdinalIgnoreCase)) _sawNonMatch = true;
            else if (_sawNonMatch && (budget is null || budget.TryTake(jobId, DateOnly.FromDateTime(now.ToLocalTime().Date)))) return UserStepStatus.Done;
        }
        return now - startedAt >= Limit ? UserStepStatus.TimedOut : UserStepStatus.Waiting;
    }

    /// <summary>The trimmed done-when text, or null when it is too short or only scheme/host words ("https://www.") to prove anything.</summary>
    private static string? Meaningful(string? text)
    {
        var trimmed = text?.Trim();
        if (trimmed is null || trimmed.Length < 4) return null;
        var words = trimmed.Split(trimmed.Where(c => !char.IsLetterOrDigit(c)).Distinct().ToArray(), StringSplitOptions.RemoveEmptyEntries);
        return words.All(GenericTokens.Contains) ? null : trimmed;
    }

    /// <summary>The page to open for the user, or null: only an absolute http or https link, whatever was stored.</summary>
    public static Uri? PageToOpen(UserStep step) =>
        step.Page is { } page && Uri.TryCreate(page, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) ? uri : null;

    /// <summary>The form kept by <see cref="WorkStore.SetUserStep"/>; "askedAt" is where <see cref="WorkStore.RecoverInterrupted"/> reads the step's age.</summary>
    public static string ToJson(UserStep step) => JsonSerializer.Serialize(step, Json);

    /// <summary>A stored step; null when there is none or it cannot be read.</summary>
    public static UserStep? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<UserStep>(json, Json) is { Step.Length: > 0 } step ? step : null; }
        catch (Exception ex) when (ex is JsonException or NotSupportedException) { return null; }
    }
}

/// <summary>How many automatic (address-match) continues each job gets a day; thread-safe, in memory only.
/// ponytail: resets on restart; persist it if a restart loop ever matters.</summary>
public sealed class AutoContinueBudget
{
    public const int PerDay = 3;
    private readonly Dictionary<string, (DateOnly Day, int Count)> _used = new(StringComparer.Ordinal);

    /// <summary>Takes one of the job's continues for <paramref name="day"/>; false once it has had <see cref="PerDay"/>.</summary>
    public bool TryTake(string jobId, DateOnly day)
    {
        lock (_used)
        {
            var count = _used.TryGetValue(jobId, out var used) && used.Day == day ? used.Count : 0;
            if (count >= PerDay) return false;
            _used[jobId] = (day, count + 1);
            return true;
        }
    }
}
