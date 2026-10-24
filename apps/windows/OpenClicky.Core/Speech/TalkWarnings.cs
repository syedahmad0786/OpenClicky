using OpenClicky.Core.Shell;

namespace OpenClicky.Core.Speech;

/// <summary>Warn before a reply plays into a speaker nobody can hear.</summary>
public static class MutedSpeakerPolicy
{
    /// <summary>True when the output endpoint is muted or its master volume is 0 (NaN counts as silent).</summary>
    public static bool ShouldWarn(bool muted, float volume) => muted || !(volume > 0f);
}

/// <summary>A talk hold that heard nothing: probably the wrong or a dead microphone.</summary>
public static class MicTroublePolicy
{
    public const long MinHoldMs = 3_000;
    public const float SilentPeak = 0.01f;

    /// <summary>The hold lasted at least 3 s and the loudest sample stayed under 0.01.</summary>
    public static bool ShouldWarn(long holdMs, float maxPeak) => holdMs >= MinHoldMs && maxPeak < SilentPeak;
}

/// <summary>The talk warning cards and their actions.</summary>
public static class TalkCards
{
    public const string MutedSpeakerId = "muted-speaker", VolumeAction = "volume50";
    public const string MicTroubleId = "mic-trouble", MicSettingsAction = "micSettings";

    public static NotchCard MutedSpeaker() => new(MutedSpeakerId, NotchCardKind.MutedSpeaker, "Your sound is off",
        "I copied my answer to your clipboard.", [new CardAction(VolumeAction, "Turn volume to 50%", CardActionStyle.Primary)], 12_000);

    public static NotchCard MicTrouble() => new(MicTroubleId, NotchCardKind.MicTrouble, "I can't hear your microphone",
        "Check that the right microphone is selected.", [new CardAction(MicSettingsAction, "Microphone settings", CardActionStyle.Primary)], 15_000);
}
