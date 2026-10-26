namespace OpenClicky.Core;

public enum TalkActionDecision { Execute, AskFirst, PreviewOnly }

public static class TalkActionPolicy
{
    /// <summary>
    /// Preview never acts. Copy only touches the clipboard, so it runs unless previewing. Open is reversible
    /// (a browser tab) so Auto runs it. Typing lands in whatever field the user is focused in and cannot be
    /// undone from here, so only FullAccess types without asking. While an agent task is actively running it may
    /// be typing too, so a talk TYPE is only previewed. A model switch changes only which of the user's own models answers
    /// next (the host checks it is one of theirs), so it runs in every mode.
    /// </summary>
    public static TalkActionDecision Decide(TalkAction action, AgentAccessMode mode, bool agentTaskRunning = false) => mode switch
    {
        _ when action.Kind == TalkActionKind.Model => TalkActionDecision.Execute,
        AgentAccessMode.Preview => TalkActionDecision.PreviewOnly,
        _ when agentTaskRunning && action.Kind == TalkActionKind.Type => TalkActionDecision.PreviewOnly,
        AgentAccessMode.FullAccess => TalkActionDecision.Execute,
        _ when action.Kind == TalkActionKind.Copy => TalkActionDecision.Execute,
        AgentAccessMode.Auto when action.Kind == TalkActionKind.Open => TalkActionDecision.Execute,
        _ => TalkActionDecision.AskFirst,
    };

    /// <summary>
    /// The mode a reply's actions run under: the one snapshotted when the reply arrived, narrowed (never widened) by a
    /// later change. No snapshot means nothing to trust, so Preview. Relies on AgentAccessMode being ordered narrow to wide.
    /// </summary>
    public static AgentAccessMode EffectiveMode(AgentAccessMode? snapshot, AgentAccessMode current) =>
        snapshot is { } s ? (AgentAccessMode)Math.Min((int)s, (int)current) : AgentAccessMode.Preview;
}
