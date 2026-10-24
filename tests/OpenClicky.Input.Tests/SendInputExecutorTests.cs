using OpenClicky.Agent;
using Xunit;

namespace OpenClicky.Input.Tests;

public class SendInputExecutorTests
{
    [Theory]
    [InlineData("first\r\nsecond", "first\rsecond")]
    [InlineData("first\nsecond", "first\nsecond")]
    [InlineData("first\rsecond", "first\rsecond")]
    public void WindowsInputText_CollapsesOnlyCrLfPairs(string text, string expected) =>
        Assert.Equal(expected, new string(SendInputExecutor.WindowsInputText(text).ToArray()));
}
