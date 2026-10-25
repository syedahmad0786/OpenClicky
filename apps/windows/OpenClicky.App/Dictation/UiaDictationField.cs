using OpenClicky.Agent;
using OpenClicky.Core.Dictation;

namespace OpenClicky.App;

/// <summary>The captured field over UI Automation: a 400 ms context read, and a verified insert only while it still has focus.</summary>
internal sealed class UiaDictationField(DictationTarget target, IAgentActionExecutor executor, Func<IEnumerable<string>> exclusions) : IDictationField
{
    private static readonly TimeSpan ContextBudget = TimeSpan.FromMilliseconds(400);

    public Task<DictationFieldContext?> ReadContextAsync(CancellationToken ct) => DictationContextReader.ReadAsync(target, ContextBudget, ct);

    public async Task<FieldInsertOutcome> InsertAsync(string text, CancellationToken ct)
    {
        var result = await target.InsertAsync(text, executor, ct, requireCurrentFocus: true, exclusions: exclusions()).ConfigureAwait(false);
        return new(result.Verified, result.ValueBefore, result.FinalText, result.Message, result.InputAttempted);
    }
}
