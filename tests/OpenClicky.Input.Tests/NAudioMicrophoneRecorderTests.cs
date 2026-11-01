using OpenClicky.Input;
using Xunit;

namespace OpenClicky.Input.Tests;

public class NAudioMicrophoneRecorderTests
{
    [Fact]
    public void CaptureCreation_DoesNotCaptureCallerSynchronizationContext()
    {
        var caller = new SynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(caller);
        try
        {
            var seenByCapture = NAudioMicrophoneRecorder.WithoutSynchronizationContext(() => SynchronizationContext.Current);

            Assert.Null(seenByCapture);
            Assert.Same(caller, SynchronizationContext.Current);
        }
        finally { SynchronizationContext.SetSynchronizationContext(null); }
    }
}
