using OpenClicky.Core.Shell;

namespace OpenClicky.Core.Onboarding;

/// <summary>The first-run tour's position. Pure state: the host starts demos, persists the first-run flag and ends the tour.</summary>
public sealed class OnboardingFlow
{
    public static IReadOnlyList<OnboardingStep> Steps { get; } = Enum.GetValues<OnboardingStep>();

    private int _index;
    private bool _busy;

    public bool Active { get; private set; }
    public OnboardingStep Step => Steps[_index];
    public bool StepDone { get; private set; }

    /// <summary>A demo is running: Next and Back are blocked. Setting it while no tour runs does nothing.</summary>
    public bool Busy
    {
        get => _busy;
        set => _busy = Active && value;
    }

    public void Start()
    {
        Active = true;
        _index = 0;
        StepDone = false;
        _busy = false;
    }

    public bool Next() => Move(1);

    public bool Back() => Move(-1);

    /// <summary>True only for the step shown now, once.</summary>
    public bool MarkDone(OnboardingStep step)
    {
        if (!Active || step != Step || StepDone) return false;
        StepDone = true;
        return true;
    }

    public void End()
    {
        Active = false;
        _busy = false;
    }

    public OnboardingView? View => Active ? new OnboardingView(Step, _index, Steps.Count, StepDone, _busy) : null;

    private bool Move(int by)
    {
        var to = _index + by;
        if (!Active || _busy || to < 0 || to >= Steps.Count) return false;
        _index = to;
        StepDone = false;
        return true;
    }
}
