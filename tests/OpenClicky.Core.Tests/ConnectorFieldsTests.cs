using OpenClicky.Core.Settings;

namespace OpenClicky.Core.Tests;

public class ConnectorFieldsTests
{
    [Fact]
    public void ParsesHeaders()
    {
        Assert.True(ConnectorFields.TryParseHeaders("X-Team: design\r\n\nX-Env: prod:blue", ConnectorAuth.None, out var headers, out _));
        Assert.Equal(2, headers.Count);
        Assert.Equal("design", headers["X-Team"]);
        Assert.Equal("prod:blue", headers["x-env"]);
        Assert.True(ConnectorFields.TryParseHeaders(null, ConnectorAuth.None, out headers, out _));
        Assert.Empty(headers);
    }

    [Theory]
    [InlineData("Bad Header: x", "Write each header as Name: value on its own line.")]
    [InlineData("NoColonHere", "Write each header as Name: value on its own line.")]
    [InlineData("X-A:", "Write each header as Name: value on its own line.")]
    [InlineData("Host: x", "Header Host can't be set here.")]
    [InlineData("content-type: x", "Header content-type can't be set here.")]
    [InlineData("Mcp-Session-Id: x", "Header Mcp-Session-Id can't be set here.")]
    [InlineData("MCP-Protocol-Version: x", "Header MCP-Protocol-Version can't be set here.")]
    [InlineData("X-A: bell\u0007", "Write each header as Name: value on its own line.")]
    [InlineData("X-A: 1\nx-a: 2", "Header x-a is listed twice.")]
    public void RejectsBadHeaders(string text, string expected)
    {
        Assert.False(ConnectorFields.TryParseHeaders(text, ConnectorAuth.None, out var headers, out var error));
        Assert.Equal(expected, error);
        Assert.Empty(headers);
    }

    [Fact]
    public void RejectsTooManyHeadersAndLongValues()
    {
        var seventeen = string.Join("\n", Enumerable.Range(0, 17).Select(i => $"X-H{i}: v"));
        Assert.False(ConnectorFields.TryParseHeaders(seventeen, ConnectorAuth.None, out _, out var error));
        Assert.Equal("Up to 16 headers.", error);
        var sixteen = string.Join("\n", Enumerable.Range(0, 16).Select(i => $"X-H{i}: v"));
        Assert.True(ConnectorFields.TryParseHeaders(sixteen, ConnectorAuth.None, out _, out _));
        Assert.False(ConnectorFields.TryParseHeaders("X-A: " + new string('v', 4_001), ConnectorAuth.None, out _, out _));
        Assert.True(ConnectorFields.TryParseHeaders("X-A: " + new string('v', 4_000), ConnectorAuth.None, out _, out _));
    }

    [Theory]
    [InlineData(ConnectorAuth.ApiKey, false)]
    [InlineData(ConnectorAuth.Browser, false)]
    [InlineData(ConnectorAuth.None, true)]
    public void AuthorizationOnlyWhenNoOtherAuth(ConnectorAuth auth, bool allowed)
    {
        Assert.Equal(allowed, ConnectorFields.TryParseHeaders("Authorization: Bearer x", auth, out _, out var error));
        if (!allowed) Assert.Equal("Header Authorization can't be set here.", error);
    }

    [Fact]
    public void ParsesEnvironment()
    {
        Assert.True(ConnectorFields.TryParseEnvironment("API_TOKEN=abc=d\n\n_X1=\n", out var env, out _));
        Assert.Equal("abc=d", env["API_TOKEN"]);
        Assert.Equal("", env["_X1"]);
        Assert.True(ConnectorFields.TryParseEnvironment(null, out env, out _));
        Assert.Empty(env);
    }

    [Theory]
    [InlineData("1BAD=x", "Write each variable as NAME=value on its own line.")]
    [InlineData("NOEQUALS", "Write each variable as NAME=value on its own line.")]
    [InlineData("A B=x", "Write each variable as NAME=value on its own line.")]
    [InlineData("A=\u0001", "Write each variable as NAME=value on its own line.")]
    [InlineData("A=1\na=2", "Variable a is listed twice.")]
    public void RejectsBadEnvironment(string text, string expected)
    {
        Assert.False(ConnectorFields.TryParseEnvironment(text, out var env, out var error));
        Assert.Equal(expected, error);
        Assert.Empty(env);
    }

    [Fact]
    public void RejectsTooManyVariables()
    {
        var many = string.Join("\n", Enumerable.Range(0, 33).Select(i => $"V{i}=1"));
        Assert.False(ConnectorFields.TryParseEnvironment(many, out _, out var error));
        Assert.Equal("Up to 32 variables.", error);
        Assert.False(ConnectorFields.TryParseEnvironment("A=" + new string('v', 4_001), out _, out _));
    }

    [Fact]
    public void ParsesArguments()
    {
        Assert.True(ConnectorFields.TryParseArguments("-y\n\n  @scope/server \r\n   \n--flag=a b", out var args, out _));
        Assert.Equal(["-y", "@scope/server", "--flag=a b"], args);
        Assert.True(ConnectorFields.TryParseArguments(null, out args, out _));
        Assert.Empty(args);
    }

    [Fact]
    public void RejectsBadArguments()
    {
        var many = string.Join("\n", Enumerable.Range(0, 33).Select(i => "a" + i));
        Assert.False(ConnectorFields.TryParseArguments(many, out _, out var error));
        Assert.Equal("Up to 32 arguments.", error);
        Assert.True(ConnectorFields.TryParseArguments(string.Join("\n", Enumerable.Range(0, 32).Select(i => "a" + i)), out _, out _));
        Assert.False(ConnectorFields.TryParseArguments(new string('a', 1_001), out _, out _));
        Assert.True(ConnectorFields.TryParseArguments(new string('a', 1_000), out _, out _));
        Assert.False(ConnectorFields.TryParseArguments("ok\nbad\u0007", out _, out _));
    }

    [Theory]
    [InlineData("npx", "npx")]
    [InlineData("  C:\\Tools\\server.exe ", "C:\\Tools\\server.exe")]
    public void AcceptsCommands(string input, string expected)
    {
        Assert.True(ConnectorFields.TryValidateCommand(input, out var clean, out _));
        Assert.Equal(expected, clean);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a\tb")]
    public void RejectsCommands(string? input)
    {
        Assert.False(ConnectorFields.TryValidateCommand(input, out _, out var error));
        Assert.Equal("Give the command to run, like npx.", error);
        Assert.False(ConnectorFields.TryValidateCommand(new string('c', 261), out _, out _));
        Assert.True(ConnectorFields.TryValidateCommand(new string('c', 260), out _, out _));
    }

    [Fact]
    public void ValidatesLabels()
    {
        Assert.True(ConnectorFields.TryValidateLabel("Personal", out var label, out _));
        Assert.Equal("Personal", label);
        Assert.True(ConnectorFields.TryValidateLabel(new string('x', 30), out label, out _));
        Assert.True(ConnectorFields.TryValidateLabel("", out label, out _));
        Assert.Null(label);
        Assert.True(ConnectorFields.TryValidateLabel(null, out label, out _));
        Assert.Null(label);
        Assert.False(ConnectorFields.TryValidateLabel(new string('x', 31), out label, out var error));
        Assert.Equal("Keep the label to 30 characters.", error);
        Assert.Null(label);
        Assert.False(ConnectorFields.TryValidateLabel("a\nb", out _, out _));
    }
}
