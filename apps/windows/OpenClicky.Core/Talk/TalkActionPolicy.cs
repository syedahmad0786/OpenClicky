namespace OpenClicky.Core;

public enum TalkActionDecision { Execute, AskFirst, PreviewOnly }

public static class TalkActionPolicy
{
    /// <summary>
    /// Preview never acts. Copy only touches the clipboard, so it runs unless previewing. Open is reversible
    /// (a browser tab) so Auto runs it. Typing lands in whatever field the user is focused in and cannot be
    /// undone from here, so only FullAccess types without asking.
    /// </summary>
    public static TalkActionDecision Decide(TalkAction action, AgentAccessMode mode) => mode switch
    {
        AgentAccessMode.Preview => TalkActionDecision.PreviewOnly,
        AgentAccessMode.FullAccess => TalkActionDecision.Execute,
        _ when action.Kind == TalkActionKind.Copy => TalkActionDecision.Execute,
        AgentAccessMode.Auto when action.Kind == TalkActionKind.Open => TalkActionDecision.Execute,
        _ => TalkActionDecision.AskFirst,
    };
}
