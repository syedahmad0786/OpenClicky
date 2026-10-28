using OpenClicky.Core.Work;

namespace OpenClicky.Core.Tests;

public sealed class JobAdmissionTests
{
    [Fact]
    public void StartsUpToFive()
    {
        var admission = new JobAdmission();
        for (var i = 1; i <= 5; i++) Assert.True(admission.TryStart("j" + i));
        Assert.Equal(5, admission.Running);
        Assert.Empty(admission.Queued);
    }

    [Fact]
    public void SixthQueues()
    {
        var admission = new JobAdmission();
        for (var i = 1; i <= 5; i++) admission.TryStart("j" + i);
        Assert.False(admission.TryStart("j6"));
        admission.Enqueue("j6");
        Assert.Equal(5, admission.Running);
        Assert.Equal(["j6"], admission.Queued);
        Assert.True(admission.Contains("j6"));
    }

    [Fact]
    public void FinishStartsNextInOrder()
    {
        var admission = new JobAdmission(max: 2);
        admission.TryStart("a");
        admission.TryStart("b");
        admission.Enqueue("c");
        admission.Enqueue("d");

        Assert.Equal("c", admission.Finish("a"));
        Assert.Equal(2, admission.Running);
        Assert.Equal(["d"], admission.Queued);
        Assert.False(admission.TryStart("e")); // c took the freed slot, so e cannot jump the queue
        Assert.Equal("d", admission.Finish("b"));
        Assert.Null(admission.Finish("c"));
        Assert.Equal(1, admission.Running);
        Assert.Null(admission.Finish("c")); // finishing twice changes nothing
        Assert.Equal(1, admission.Running);
    }

    [Fact]
    public void StartOrQueueIsOneStep()
    {
        var admission = new JobAdmission(max: 1);
        Assert.Equal(JobAdmission.Outcome.Started, admission.TryStartOrEnqueue("a"));
        Assert.Equal(JobAdmission.Outcome.Queued, admission.TryStartOrEnqueue("b"));
        Assert.Equal(JobAdmission.Outcome.Refused, admission.TryStartOrEnqueue("a"));
        Assert.Equal(JobAdmission.Outcome.Refused, admission.TryStartOrEnqueue("b"));
        // A run finishing right after b was refused a slot hands it the slot: b is never stranded behind an empty queue.
        Assert.Equal("b", admission.Finish("a"));
        Assert.Equal(1, admission.Running);
        Assert.Empty(admission.Queued);
    }

    [Fact]
    public void SameJobNeverTwice()
    {
        var admission = new JobAdmission(max: 1);
        Assert.True(admission.TryStart("a"));
        Assert.False(admission.TryStart("a"));
        admission.Enqueue("a");
        Assert.Empty(admission.Queued);

        admission.Enqueue("b");
        admission.Enqueue("b");
        Assert.Equal(["b"], admission.Queued);
        Assert.False(admission.TryStart("b"));

        Assert.Null(admission.Finish("b")); // a queued job that is stopped leaves the queue without starting anything
        Assert.Empty(admission.Queued);
        Assert.Equal(1, admission.Running);
    }
}
