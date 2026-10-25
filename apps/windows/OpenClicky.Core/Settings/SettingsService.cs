using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenClicky.Core.Input;
using OpenClicky.Core.Shell;

namespace OpenClicky.Core.Settings;

/// <summary>
/// Applies settings changes for any surface (the notch page, native prompts): each change is made on a copy, the copy
/// is saved, and only then is the change applied to the live settings, so a failed save never leaves them half-changed.
/// Every outcome re-pushes state so the page shows what is really saved.
/// </summary>
/// <param name="save">Persists a full copy (secrets already cleared). Throws on I/O failure.</param>
/// <param name="confirmAccess">Shows the native Auto/Full access warning; true only when the user accepted.</param>
/// <param name="applied">Called with the patch key after it took effect, for side effects (TTS on/off, history reload…).</param>
/// <param name="trace">Diagnostics (keys and results only, never values).</param>
public sealed class SettingsService(AppSettings settings, Action<AppSettings> save, Func<AgentAccessMode, bool> confirmAccess,
    Action<string> toast, Action push, Action<string>? applied = null, Action<string>? trace = null)
{
    public PatchResult Patch(string key, JsonElement value)
    {
        var result = Attempt(key, value, confirmed: false, out var error);
        if (result == PatchResult.RequiresConfirmation && confirmAccess(RequestedMode(value)))
            result = Attempt(key, value, confirmed: true, out error);
        if (result == PatchResult.Rejected) toast(error ?? "That setting could not be changed.");
        push();
        return result;
    }

    /// <summary>A change that is not a page patch (agent folder, reset shortcuts). False (toasted) when it could not be saved.</summary>
    public bool Change(Action<AppSettings> edit)
    {
        var draft = SettingsStore.CloneWithoutSecrets(settings);
        edit(draft);
        if (!TrySave(draft, out var error)) { toast(error); push(); return false; }
        edit(settings);
        push();
        return true;
    }

    private PatchResult Attempt(string key, JsonElement value, bool confirmed, out string? error)
    {
        var draft = SettingsStore.CloneWithoutSecrets(settings);
        var result = SettingsPatch.TryApply(draft, key, value, out error, confirmed);
        if (result != PatchResult.Applied) return result;
        if (!TrySave(draft, out var saveError)) { error = saveError; return PatchResult.Rejected; }
        // Same input on equal settings gives the same outcome; anything else means the live settings moved meanwhile.
        var live = SettingsPatch.TryApply(settings, key, value, out _, confirmed);
        if (live != PatchResult.Applied) trace?.Invoke($"settings_reapply_mismatch key={key} result={live}");
        applied?.Invoke(key);
        return PatchResult.Applied;
    }

    private bool TrySave(AppSettings draft, out string error)
    {
        error = "";
        try { save(draft); return true; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            error = "Could not save settings: " + e.Message;
            return false;
        }
    }

    private static AgentAccessMode RequestedMode(JsonElement value) =>
        value.ValueKind == JsonValueKind.String && value.GetString() == "full" ? AgentAccessMode.FullAccess : AgentAccessMode.Auto;
}

public enum ConnectorAuth { None, ApiKey, Browser }

/// <summary>A Windows privacy area agents may reach: its consent-store key and its ms-settings page.</summary>
public sealed record ReachTarget(string Id, string Label, string ConsentKey, string Page);

/// <summary>Pure helpers behind the settings.action handlers (argument parsing, id and schedule mapping).</summary>
public static class SettingsActionArgs
{
    /// <summary>The reach rows, in page order. ConsentKey is under HKCU\…\CapabilityAccessManager\ConsentStore.</summary>
    public static readonly IReadOnlyList<ReachTarget> Reach =
    [
        new("documents", "Documents", "documentsLibrary", "privacy-documents"),
        new("pictures", "Pictures", "picturesLibrary", "privacy-pictures"),
        new("contacts", "Contacts", "contacts", "privacy-contacts"),
        new("calendar", "Calendar", "appointments", "privacy-calendar"),
        new("microphone", "Microphone", "microphone", "privacy-microphone"),
        new("camera", "Camera", "webcam", "privacy-webcam"),
        new("location", "Location", "location", "privacy-location"),
    ];

    /// <summary>Only the seven known pages; anything else from the page is ignored.</summary>
    public static string? PrivacyUri(string? id) => Reach.FirstOrDefault(r => r.Id == id) is { } r ? "ms-settings:" + r.Page : null;

    public static ReachState ConsentState(string? value) => value switch
    {
        "Allow" => ReachState.Allowed,
        "Deny" => ReachState.Denied,
        _ => ReachState.Unknown,
    };

    /// <summary>The secret a secret.edit arg names, with the prompt title; null for anything else.</summary>
    public static (string Name, string Title)? SecretFor(string? arg) => arg switch
    {
        "openrouter" => (SecretNames.OpenRouterApiKey, "OpenRouter API key"),
        "openai" => (SecretNames.OpenAiApiKey, "OpenAI API key"),
        _ => null,
    };

    /// <summary>"win:" + 16 hex chars of SHA-256 of the voice token id: stable, and short enough for the voice cap.</summary>
    public static string VoiceShortId(string tokenId) =>
        "win:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tokenId)))[..16].ToLowerInvariant();

    private static readonly System.Text.RegularExpressions.Regex AuthScheme =
        new(@"^[A-Za-z][A-Za-z0-9-]* \S", System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    /// <summary>
    /// Header for an API-key connector: Authorization by default. An Authorization value that does not already start
    /// with a scheme word and a space ("Bearer ", "Basic ", "Token "…) gets "Bearer ". Other headers (X-Api-Key…)
    /// carry the value as typed.
    /// </summary>
    public static (string Name, string Value) ApiKeyHeader(string? headerName, string secret)
    {
        var name = string.IsNullOrWhiteSpace(headerName) ? "Authorization" : headerName.Trim();
        var value = secret.Trim();
        if (name.Equals("Authorization", StringComparison.OrdinalIgnoreCase) && !AuthScheme.IsMatch(value))
            value = "Bearer " + value;
        return (name, value);
    }

    /// <summary>A newly typed secret, or null when the box was left empty (meaning: keep the saved one).</summary>
    public static string? TypedSecret(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>An exception message fit for a toast: ArgumentException's " (Parameter 'x')" suffix removed.</summary>
    public static string UserMessage(Exception e) =>
        e is ArgumentException { ParamName: { } name } ? e.Message.Replace($" (Parameter '{name}')", "", StringComparison.Ordinal) : e.Message;

    public static Routine Pause(Routine routine) => routine with { IsPaused = true };

    /// <summary>Resuming schedules the next slot after now, so a long pause does not fire a stale run at once.</summary>
    public static Routine Resume(Routine routine, DateTimeOffset now, TimeZoneInfo zone) =>
        routine with { IsPaused = false, NextRunAt = RoutineSchedule.NextAfter(routine, now, zone) };

    /// <summary>Routines whose job still exists, titled by that job. Paused routines show no next run.</summary>
    public static IReadOnlyList<RoutineView> RoutineViews(IReadOnlyList<Routine> routines, IReadOnlyList<WorkJob> jobs)
    {
        var byId = jobs.ToDictionary(j => j.Id, StringComparer.Ordinal);
        return routines.Where(r => byId.ContainsKey(r.JobId)).Select(r =>
        {
            var job = byId[r.JobId];
            var status = r.IsPaused ? RoutineStatus.Paused
                : job.Status == WorkJobStates.Running ? RoutineStatus.Running
                : r.ConsecutiveFailures > 0 ? RoutineStatus.Failed
                : RoutineStatus.Scheduled;
            return new RoutineView(r.Id, string.IsNullOrWhiteSpace(job.Title) ? "Job" : job.Title.Trim(), RoutineSchedule.Describe(r),
                r.IsPaused ? null : ShellStateBuilder.Iso(r.NextRunAt), r.LastRunAt is { } last ? ShellStateBuilder.Iso(last) : null, status);
        }).ToList();
    }

    public static string? HotkeyKey(string? row) => row switch
    {
        "talk" => "shortcuts.talk",
        "text" => "shortcuts.text",
        "dictate" => "shortcuts.dictate",
        "handsFree" => "shortcuts.handsFree",
        _ => null,
    };

    public static ShortcutAction? HotkeyAction(string? row) => row switch
    {
        "talk" => ShortcutAction.Talk,
        "text" => ShortcutAction.Text,
        "dictate" => ShortcutAction.Dictate,
        "handsFree" => ShortcutAction.HandsFree,
        _ => null,
    };

    /// <summary>The settings.patch value for a recorded binding, so it goes through the same validation and conflict check.</summary>
    public static JsonElement HotkeyPatchValue(HotkeyBinding binding) =>
        JsonSerializer.SerializeToElement(new { keys = binding.Keys, trigger = binding.Trigger == HotkeyTrigger.Hold ? "hold" : "doubleTap" });

    /// <summary>All four shortcuts back to their defaults.</summary>
    public static void ResetHotkeys(AppSettings s)
    {
        s.TalkHotkey = HotkeyBinding.DefaultTalk;
        s.TextHotkey = HotkeyBinding.DefaultText;
        s.DictateHotkey = HotkeyBinding.DefaultDictate;
        s.HandsFreeHotkey = HotkeyBinding.DefaultHandsFree;
    }
}
