using System.Text.Json;

namespace OpenClicky.Core.Work;

public enum UserStepStatus { Waiting, Done, TimedOut }

/// <summary>What to do with a stored step when a needs-you job is picked up again (after a restart, or when a start of it was undone).</summary>
public enum UserStepResume { Watch, TimedOut, Unreadable }

/// <summary>
/// Decides whether the step a job is waiting on the user for is done: the browser address now contains the step's
/// "done when" text, or the user said so (which the caller handles), or six hours passed. The address is untrusted and private:
/// it is only compared here, never stored or logged.
/// </summary>
public sealed class UserStepWatch(UserStep step, DateTimeOffset startedAt)
{
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

    /// <summary>A step without "done when" text never completes by itself; only the user's "I'm done" (or the limit) ends it.</summary>
    public UserStepStatus Check(DateTimeOffset now, string? currentAddress)
    {
        if (currentAddress is not null && Step.DoneWhenUrlContains is { } text && !string.IsNullOrWhiteSpace(text)
            && currentAddress.Contains(text, StringComparison.OrdinalIgnoreCase)) return UserStepStatus.Done;
        return now - startedAt >= Limit ? UserStepStatus.TimedOut : UserStepStatus.Waiting;
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
