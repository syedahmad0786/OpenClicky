using OpenClicky.Core;

namespace OpenClicky.Providers;

/// <summary>
/// Opt-in offline Talk fixture for local smoke (no network / no API keys).
/// Activated when <c>OPENCLICKY_OFFLINE_TALK=1</c> (see <see cref="ProviderFactory"/>).
/// </summary>
public sealed class OfflineTalkClient : ITalkClient
{
    public const string EnvVarName = "OPENCLICKY_OFFLINE_TALK";

    /// <summary>Spoken text portion of the canned reply (no POINT tag).</summary>
    public const string SpokenText =
        "Here's the center of this screen. This demo works without an AI account.";

    /// <summary>Label embedded in the canned POINT tag.</summary>
    public const string PointLabel = "screen center";

    public static bool IsEnvEnabled()
    {
        var value = Environment.GetEnvironmentVariable(EnvVarName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Equals("1", StringComparison.Ordinal)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds a Clicky-compatible reply with a POINT near the primary/cursor capture center.
    /// Falls back to 960,540 when no screens are provided.
    /// </summary>
    public static string BuildCannedReply(IReadOnlyList<ScreenCaptureResult> screens)
    {
        var target = screens.FirstOrDefault(s => s.IsCursorMonitor) ?? screens.FirstOrDefault();
        var x = 960;
        var y = 540;
        if (target is not null)
        {
            x = Math.Max(1, target.PixelWidth / 2);
            y = Math.Max(1, target.PixelHeight / 2);
        }

        return $"{SpokenText} [POINT:{x},{y}:{PointLabel}]";
    }

    public Task<string> CompleteVisionTalkAsync(
        string systemPrompt,
        IReadOnlyList<(string role, string text)> history,
        string userTranscript,
        IReadOnlyList<ScreenCaptureResult> screens,
        string model,
        CancellationToken cancellationToken = default)
    {
        _ = systemPrompt;
        _ = history;
        _ = userTranscript;
        _ = model;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(BuildCannedReply(screens));
    }
}
