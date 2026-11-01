namespace OpenClicky.Core;

public enum TaskVoiceCommandKind { None, Stop, Pause, Resume, Note, Remember, ChangeTask }
public sealed record TaskVoiceCommand(TaskVoiceCommandKind Kind, string Text = "");

public static class TaskVoiceCommands
{
    public static TaskVoiceCommand Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > 12000) return new(TaskVoiceCommandKind.None);
        var command = text.Trim().TrimEnd('.', '!', '?').ToLowerInvariant();
        if (command is "stop" or "stop task" or "cancel task") return new(TaskVoiceCommandKind.Stop);
        if (command is "pause" or "pause task") return new(TaskVoiceCommandKind.Pause);
        if (command is "resume" or "resume task" or "continue task") return new(TaskVoiceCommandKind.Resume);
        foreach (var (prefix, kind) in new[] { ("note:", TaskVoiceCommandKind.Note), ("take a note", TaskVoiceCommandKind.Note),
            ("remember:", TaskVoiceCommandKind.Remember), ("remember this", TaskVoiceCommandKind.Remember), ("change task", TaskVoiceCommandKind.ChangeTask) })
        {
            var request = text.Trim();
            if (!request.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (!prefix.EndsWith(':') && request.Length > prefix.Length && !" :,-".Contains(request[prefix.Length])) continue;
            return new(kind, request[prefix.Length..].TrimStart(' ', ':', ',', '-').Trim());
        }
        return new(TaskVoiceCommandKind.None);
    }
}
