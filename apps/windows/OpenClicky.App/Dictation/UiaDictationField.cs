using OpenClicky.Agent;
using OpenClicky.Core.Dictation;
using OpenClicky.Core.Work;

namespace OpenClicky.App;

/// <summary>The captured field over UI Automation: a 400 ms context read, and a verified insert only while it still has focus. The insert
/// holds the desktop lease (holder "talk") so a job's input waits; a job that keeps the screen past a short wait leaves it unverified
/// (the text then goes to the clipboard).</summary>
internal sealed class UiaDictationField(DictationTarget target, IAgentActionExecutor executor, Func<IEnumerable<string>> exclusions, DesktopLease lease) : IDictationField
{
    private static readonly TimeSpan ContextBudget = TimeSpan.FromMilliseconds(400);

    public Task<DictationFieldContext?> ReadContextAsync(CancellationToken ct) => DictationContextReader.ReadAsync(target, ContextBudget, ct);

    public async Task<FieldInsertOutcome> InsertAsync(string text, CancellationToken ct)
    {
        using var screen = await lease.TryAcquireAsync(DesktopLease.TalkHolder, DesktopLease.TalkWait, ct).ConfigureAwait(false);
        if (screen is null) return new(false, null, null, TrayApplicationContext.ScreenBusyForText);
        var result = await target.InsertAsync(text, executor, ct, requireCurrentFocus: true, exclusions: exclusions()).ConfigureAwait(false);
        return new(result.Verified, result.ValueBefore, result.FinalText, result.Message, result.InputAttempted);
    }
}
