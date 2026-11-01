namespace OpenClicky.Agent;

/// <summary>Tool names for the Phase D computer-use loop (OpenRouter tools).</summary>
public static class AgentToolNames
{
    public const string Screenshot = "screenshot";
    public const string UiaSnapshot = "uia_snapshot";
    public const string Click = "click";
    public const string TypeText = "type_text";
    public const string Hotkey = "hotkey";
    public const string Scroll = "scroll";
    public const string Wait = "wait";
    public const string ListApps = "list_apps";
    public const string OpenApp = "open_app";
}

public sealed record AgentActionRequest(
    string ToolName,
    IReadOnlyDictionary<string, string> Arguments,
    string? Rationale);

public sealed record AgentActionResult(
    bool Success,
    string Message,
    bool RequiresConfirmation);
