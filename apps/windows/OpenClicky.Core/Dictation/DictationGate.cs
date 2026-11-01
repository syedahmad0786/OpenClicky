namespace OpenClicky.Core.Dictation;

/// <summary>Why the focused control could not be captured as a dictation field.</summary>
public enum DictationCaptureFailure { None, Password, OwnWindow, Denied, NotWritable, NoFocus }

/// <summary>Proceed without a field: the dictation still records and its words go to the clipboard.</summary>
public sealed record DictationGateDecision(bool Proceed, bool WithField, string? RefusalMessage);

public static class DictationGate
{
    /// <summary>Password fields, OpenClicky's own windows and excluded apps refuse; anything else records.</summary>
    public static DictationGateDecision Check(DictationCaptureFailure failure) => failure switch
    {
        DictationCaptureFailure.None => new(true, true, null),
        DictationCaptureFailure.NotWritable or DictationCaptureFailure.NoFocus => new(true, false, null),
        DictationCaptureFailure.Password => new(false, false, "Dictation is unavailable in password or protected fields."),
        DictationCaptureFailure.OwnWindow => new(false, false, "Dictation types into other apps. Click a field outside OpenClicky, then dictate again."),
        DictationCaptureFailure.Denied => new(false, false, "This app is excluded from dictation. Nothing was inspected."),
        _ => throw new ArgumentOutOfRangeException(nameof(failure)),
    };
}
