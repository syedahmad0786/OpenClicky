namespace OpenClicky.Core.Onboarding;

/// <summary>
/// Canned reply for the first-run draw demo. It goes through the real talk pipeline (DRAW tags, then POINT),
/// so the shapes are sized from the screenshot the pipeline will map: every point stays inside W x H.
/// </summary>
public static class OnboardingDrawDemo
{
    public const string Prompt = "Show me how you draw on my screen";
    public const string Spoken = "I can circle things, point at them and leave you a note. Press Escape and it all goes away.";

    public static string Reply(IReadOnlyList<ScreenCaptureResult> screens)
    {
        var index = screens.ToList().FindIndex(s => s.IsCursorMonitor);
        if (index < 0 && screens.Count > 0) index = 0;
        var w = index >= 0 ? screens[index].PixelWidth : 1920;
        var h = index >= 0 ? screens[index].PixelHeight : 1080;
        var m = Math.Min(w, h);
        var screen = index > 0 ? $" screen={index + 1}" : "";

        var circle = $"[DRAW:circle id=demo-circle x={w / 2} y={h / 2} r={m / 10} label=\"I circle things\"{screen}]";
        var arrow = $"[DRAW:arrow id=demo-arrow pts={w / 4},{h * 3 / 4},{w / 2 - m / 10 - m / 40},{h / 2 + m / 40}{screen}]";
        var sticky = $"[DRAW:sticky id=demo-note x={w * 3 / 4} y={h / 4} text=\"Hi! I'm OpenClicky.\" stock=butter{screen}]";
        return $"{circle}I can circle things, {arrow}point at them and {sticky}leave you a note. Press Escape and it all goes away. [POINT:none]";
    }
}

/// <summary>Talk client for the draw demo: returns the canned reply and never touches the network.</summary>
public sealed class OnboardingDrawTalkClient : ITalkClient
{
    public Task<string> CompleteVisionTalkAsync(
        string systemPrompt,
        IReadOnlyList<(string role, string text)> history,
        string userTranscript,
        IReadOnlyList<ScreenCaptureResult> screens,
        string model,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(OnboardingDrawDemo.Reply(screens));
    }
}
