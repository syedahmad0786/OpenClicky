namespace OpenClicky.Core;

public sealed record HomeRequestRoute(bool IsLocal, string Request);

public static class HomeRequestRouting
{
    public static bool RequiresTargetSwitch(string text, bool agentSelected) =>
        agentSelected || !VisualArtifactReply.IsSelfContainedRequest(text);

    public static HomeRequestRoute Resolve(string text, bool agentSelected)
    {
        ArgumentNullException.ThrowIfNull(text);
        var request = text.Trim();
        if (TaskVoiceCommands.Parse(request).Kind != TaskVoiceCommandKind.None)
            return new(true, request);
        return new(false, agentSelected ? "openclicky agent " + request : request);
    }
}
