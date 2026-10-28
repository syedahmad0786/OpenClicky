using System.Text.Json.Serialization;

namespace OpenClicky.Core.Proactive;

/// <summary>
/// The user's proactive-card choices. Every flag is off until the user turns it on in Settings; nothing in code,
/// migration or tests enables one. <see cref="CalendarConnector"/> is the connector key asked for today's events.
/// </summary>
public sealed class ProactiveSettings
{
    public bool MorningBrief { get; set; }
    public bool Suggestions { get; set; }
    public bool IntegrationNudges { get; set; }
    public bool MeetingCountdown { get; set; }
    public string? CalendarConnector { get; set; }

    [JsonIgnore] public bool AnyOn => MorningBrief || Suggestions || IntegrationNudges || MeetingCountdown;
}
