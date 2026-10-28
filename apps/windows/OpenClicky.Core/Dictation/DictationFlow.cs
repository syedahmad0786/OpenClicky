namespace OpenClicky.Core.Dictation;

/// <summary>The field the user was typing in when dictation started.</summary>
public interface IDictationField
{
    Task<DictationFieldContext?> ReadContextAsync(CancellationToken ct);
    Task<FieldInsertOutcome> InsertAsync(string text, CancellationToken ct);
}

/// <summary>Verified means the field's value was read back and shows the text; InputSent, that keys went out (the text may be in the field).</summary>
public sealed record FieldInsertOutcome(bool Verified, string? ValueBefore, string? ValueAfter, string Message, bool InputSent = false);

public enum DictationDeliveryKind { Inserted, Clipboard, Nothing }

public sealed record DictationDelivery(DictationDeliveryKind Kind, string Text, FieldInsertOutcome? Insert);

/// <param name="FallbackBefore">The text the seam is fitted after when the field's context cannot be read (the session's previous phrase).</param>
public sealed record DictationOptions(bool Cleanup, IReadOnlyList<string> Dictionary, int CleanupTimeoutMs = 6000, string? FallbackBefore = null);

public static class DictationFlow
{
    /// <summary>
    /// Trim, read the field's context, tidy up (bounded by <see cref="DictationOptions.CleanupTimeoutMs"/>; any failure keeps
    /// the raw text), fit at the caret, then insert, falling back to the clipboard when there is no field or the insert is
    /// not verified. Throws <see cref="OperationCanceledException"/> only when <paramref name="ct"/> is cancelled.
    /// </summary>
    public static async Task<DictationDelivery> DeliverAsync(string raw, IDictationField? field, IDictationCleaner? cleaner,
        DictationOptions options, Action<string> copyToClipboard, CancellationToken ct)
    {
        var text = raw?.Trim() ?? "";
        if (text.Length == 0) return new(DictationDeliveryKind.Nothing, "", null);
        var spoken = text;
        ct.ThrowIfCancellationRequested();

        DictationFieldContext? context = null;
        if (field is not null)
        {
            // A context read failure must not lose the dictation: carry on without context.
            try { context = await field.ReadContextAsync(ct); }
            catch (Exception) when (!ct.IsCancellationRequested) { }
        }

        if (options.Cleanup && cleaner is not null && LongEnoughToTidy(text))
            text = await TryCleanAsync(text, context, cleaner, options, ct) ?? text;

        // Unread context: fit after the session's previous phrase, so phrases never glue together. Line breaks the speaker did
        // not dictate (a tidy-up adding them) are folded, since they would be typed as Enter.
        var fitted = DictationTextFit.Fit(text, context?.BeforeText ?? options.FallbackBefore ?? "", context?.AfterText ?? "",
            (context?.SingleLine ?? false) || !spoken.Contains('\n'));
        ct.ThrowIfCancellationRequested();

        FieldInsertOutcome? insert = null;
        if (field is not null)
        {
            // An insert that throws counts as unverified, so the text still reaches the clipboard.
            try { insert = await field.InsertAsync(fitted, ct); }
            catch (Exception ex) when (!ct.IsCancellationRequested) { insert = new(false, null, null, ex.Message); }
            if (insert.Verified) return new(DictationDeliveryKind.Inserted, fitted, insert);
        }
        copyToClipboard(fitted);
        return new(DictationDeliveryKind.Clipboard, fitted, insert);
    }

    private static async Task<string?> TryCleanAsync(string text, DictationFieldContext? context, IDictationCleaner cleaner,
        DictationOptions options, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(options.CleanupTimeoutMs);
        try
        {
            // WaitAsync bounds a cleaner that ignores its token.
            var cleaned = await cleaner.CleanAsync(text, context, options.Dictionary, timeout.Token).WaitAsync(timeout.Token);
            return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned.Trim();
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    // More than 2 words, or at least 6 characters of a script written without spaces (CJK, kana, Thai).
    private static bool LongEnoughToTidy(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length > 2
        || text.Count(DictationTextFit.IsNoSpaceScript) >= 6;
}
