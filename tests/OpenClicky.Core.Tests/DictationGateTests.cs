using OpenClicky.Core.Dictation;

namespace OpenClicky.Core.Tests;

public class DictationGateTests
{
    [Fact]
    public void PasswordAndOwnWindowRefuse()
    {
        foreach (var failure in new[] { DictationCaptureFailure.Password, DictationCaptureFailure.OwnWindow, DictationCaptureFailure.Denied })
        {
            var decision = DictationGate.Check(failure);
            Assert.False(decision.Proceed);
            Assert.False(decision.WithField);
            Assert.False(string.IsNullOrWhiteSpace(decision.RefusalMessage));
        }

        foreach (var failure in new[] { DictationCaptureFailure.NotWritable, DictationCaptureFailure.NoFocus })
            Assert.Equal(new DictationGateDecision(true, false, null), DictationGate.Check(failure));

        Assert.Equal(new DictationGateDecision(true, true, null), DictationGate.Check(DictationCaptureFailure.None));
    }
}
