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

/// <summary>When the captions bar is told to clear (it fades 1.5 s later unless another word arrives).</summary>
public static class CaptionsPolicy
{
    /// <summary>
    /// A sentence ended: clear unless the turn is still listening or composing its reply (more sentences may follow). Once the
    /// reply is complete (Responding, Pointing with marks shown for seconds, Idle) the last sentence's end starts the fade.
    /// </summary>
    public static bool ClearOnSpeechEnded(CompanionVoiceState state) =>
        state is not (CompanionVoiceState.Listening or CompanionVoiceState.Processing);

    /// <summary>Listening (a new press or a barge-in) and Idle (the turn ended or was stopped) clear the old reply's words.</summary>
    public static bool ClearOnState(CompanionVoiceState state) =>
        state is CompanionVoiceState.Listening or CompanionVoiceState.Idle;
}

/// <summary>The talk warning cards and their actions.</summary>
public static class TalkCards
{
    public const string MutedSpeakerId = "muted-speaker", VolumeAction = "volume50";
    public const string MicTroubleId = "mic-trouble", MicSettingsAction = "micSettings";

    /// <param name="copied">The reply is on the clipboard: only then does the card say so.</param>
    public static NotchCard MutedSpeaker(bool copied = false) => new(MutedSpeakerId, NotchCardKind.MutedSpeaker, "Your sound is off",
        copied ? "I copied my answer to your clipboard." : null, [new CardAction(VolumeAction, "Turn volume to 50%", CardActionStyle.Primary)], 12_000);

    public static NotchCard MicTrouble() => new(MicTroubleId, NotchCardKind.MicTrouble, "I can't hear your microphone",
        "Check that the right microphone is selected.", [new CardAction(MicSettingsAction, "Microphone settings", CardActionStyle.Primary)], 15_000);
}
