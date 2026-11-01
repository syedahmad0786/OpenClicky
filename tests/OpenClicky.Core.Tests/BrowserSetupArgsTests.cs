using OpenClicky.Core.Browser;
using Xunit;

namespace OpenClicky.Core.Tests;

public sealed class BrowserSetupArgsTests
{
    public static TheoryData<string, BrowserSetupRequest> CardArgs => new()
    {
        { """{"op":"connect","browser":"chrome","profile":"Default"}""", new("connect", Browser: "chrome", Profile: "Default") },
        { """{"op":"connect","browser":"edge","profile":"Profile 2"}""", new("connect", Browser: "edge", Profile: "Profile 2") },
        { """{"op":"connect","browser":"chrome-beta","profile":""}""", new("connect", Browser: "chrome-beta", Profile: "") },
        { """{"op":"openExtensions"}""", new("openExtensions") },
        { """{"op":"showFolder"}""", new("showFolder") },
        { """{"op":"copyPath"}""", new("copyPath") },
        { """{"op":"select","id":"p2"}""", new("select", Id: "p2") },
        { """{"op":"get","browser":"chrome"}""", new("get", Browser: "chrome") },
        { """{"op":"get","browser":"edge"}""", new("get", Browser: "edge") },
        { """{"op":"get","browser":"brave"}""", new("get", Browser: "brave") },
        { "disconnect", new("disconnect") },
    };

    [Theory]
    [MemberData(nameof(CardArgs))]
    public void ParsesEveryOp(string arg, BrowserSetupRequest expected) => Assert.Equal(expected, BrowserSetupArgs.Parse(arg));

    /// <summary>A connect arg padded with an ignored field to exactly <paramref name="length"/> characters.</summary>
    private static string Padded(int length)
    {
        const string head = """{"op":"connect","browser":"chrome","profile":"Default","x":""";
        return head + "\"" + new string('a', length - head.Length - 3) + "\"}";
    }

    public static TheoryData<string?> Broken => new()
    {
        null, "", """{"op":"install"}""", """{"op":"end"}""", "not json", """{"op":"connect","browser":"chrome" """, """["connect"]""",
        Padded(513),
        """{"op":"connect","browser":"chr\\ome","profile":"Default"}""",
        """{"op":"connect","browser":"chr\"ome","profile":"Default"}""",
        """{"op":"connect","profile":"Default"}""",
        """{"op":"connect","browser":7,"profile":"Default"}""",
        """{"op":"connect","browser":"chrome","profile":".."}""",
        """{"op":"connect","browser":"chrome","profile":"..\\Default"}""",
        """{"op":"connect","browser":"chrome","profile":"Profile\\1"}""",
        """{"op":"connect","browser":"chrome","profile":"Profile/1"}""",
        """{"op":"connect","browser":"chrome","profile":"Pro\"file"}""",
        """{"op":"select"}""",
        """{"op":"select","id":"a\"b"}""",
        """{"op":"get","browser":"opera"}""",
    };

    [Theory]
    [MemberData(nameof(Broken))]
    public void RejectsUnknownOpLongOrBrokenArgs(string? arg) => Assert.Null(BrowserSetupArgs.Parse(arg));

    [Fact]
    public void AcceptsArgsUpToTheLengthCap()
    {
        Assert.Equal(513, Padded(513).Length);
        Assert.Equal(new BrowserSetupRequest("connect", Browser: "chrome", Profile: "Default"), BrowserSetupArgs.Parse(Padded(512)));
    }
}
