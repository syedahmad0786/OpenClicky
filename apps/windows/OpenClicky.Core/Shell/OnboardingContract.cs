namespace OpenClicky.Core.Shell;

// Mirror of the onboarding section of apps/companion-workspace/src/shell/contract.ts.
// tests/fixtures/onboarding.v1.json lists the wire names both test suites check.

public enum OnboardingStep { Welcome, Colour, Microphone, Screen, MicCheck, VoiceHello, DrawDemo, CircleToAsk, DriveDemo, TextMode, Dictation, Finale }

/// <summary>What the page shows of a running tour: the step, its position, whether the step is done and whether a demo is running.</summary>
public sealed record OnboardingView(OnboardingStep Step, int Index, int Count, bool StepDone, bool Busy);

public static class OnboardingContract
{
    /// <summary>Allow-listed values of the page's <c>onboarding</c> intent <c>action</c>.</summary>
    public static readonly IReadOnlyList<string> Actions =
        ["start", "next", "back", "skip", "finish", "recheck", "openScreenSettings", "drawDemo", "driveDemo"];
}
