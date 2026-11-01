namespace OpenClicky.Core.Proactive;

/// <summary>Tells the proactive layer when to stay out of the way (a call, a presentation, a full-screen app).</summary>
public interface IQuietSignal
{
    /// <summary>null when not quiet; otherwise a short reason such as "On a call" or "Presenting".</summary>
    string? QuietReason();
}
