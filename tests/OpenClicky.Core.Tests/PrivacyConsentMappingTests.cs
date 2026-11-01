using OpenClicky.Core.Settings;
using OpenClicky.Core.Shell;
using Xunit;

namespace OpenClicky.Core.Tests;

public sealed class PrivacyConsentMappingTests
{
    private static ReachState Resolve(Dictionary<string, string?> store) =>
        PrivacyConsentMapping.Resolve("microphone", path => store.GetValueOrDefault(path));

    [Fact]
    public void NonPackagedValueWinsWhenPresent() =>
        Assert.Equal(ReachState.Allowed, Resolve(new() { [@"microphone\NonPackaged"] = "Allow", ["microphone"] = "Deny" }));

    [Fact]
    public void NonPackagedDenyOverridesGlobalAllow() =>
        Assert.Equal(ReachState.Denied, Resolve(new() { [@"microphone\NonPackaged"] = "Deny", ["microphone"] = "Allow" }));

    [Fact]
    public void FallsBackToTheAreaValueWhenNonPackagedIsMissingOrEmpty()
    {
        Assert.Equal(ReachState.Denied, Resolve(new() { ["microphone"] = "Deny" }));
        Assert.Equal(ReachState.Allowed, Resolve(new() { [@"microphone\NonPackaged"] = "", ["microphone"] = "Allow" }));
    }

    [Fact]
    public void NothingReadableIsUnknown() => Assert.Equal(ReachState.Unknown, Resolve([]));
}
