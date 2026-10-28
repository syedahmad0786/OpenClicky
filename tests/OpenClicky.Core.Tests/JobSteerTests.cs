using OpenClicky.Core.Work;

namespace OpenClicky.Core.Tests;

public class JobSteerTests
{
    [Fact]
    public void AppendsToTheRequestOrTheAskSoFar()
    {
        Assert.Equal("use March", JobSteer.Append(null, "use March"));
        Assert.Equal("use March", JobSteer.Append("  ", "use March"));
        Assert.Equal("Summarise the report\nuse March", JobSteer.Append(" Summarise the report ", "use March"));
    }

    [Fact]
    public void NullOnceTheAskWouldPassTheCap()
    {
        Assert.Equal(JobSteer.MaxAsk, JobSteer.Append(new string('a', JobSteer.MaxAsk - 2), "b")!.Length);
        Assert.Null(JobSteer.Append(new string('a', JobSteer.MaxAsk - 1), "b"));
    }
}
