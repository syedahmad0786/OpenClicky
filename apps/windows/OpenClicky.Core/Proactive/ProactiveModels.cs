namespace OpenClicky.Core.Proactive;

/// <summary>A connected service the suggestion prompt may name (name and host only, never a URL path or header).</summary>
public sealed record ConnectorRef(string Key, string Name, string Host);

/// <summary>One routine run due today.</summary>
public sealed record RoutineSlot(string Title, DateTimeOffset At);
