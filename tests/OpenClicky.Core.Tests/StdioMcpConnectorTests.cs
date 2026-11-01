using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using OpenClicky.Core;
using OpenClicky.Providers;

namespace OpenClicky.Core.Tests;

public class StdioMcpConnectorTests
{
    private static readonly TimeSpan Quick = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ListsAndCallsOverLines()
    {
        await using var pipe = await FakeServer.StartAsync();
        using var connector = new StdioMcpConnector("local", "Local", pipe.Client, pipe.Client, Quick);
        var server = Task.Run(async () =>
        {
            await pipe.ReplyAsync(await pipe.ReadAsync(), new { protocolVersion = "2025-06-18", capabilities = new { }, serverInfo = new { name = "fake" } });
            Assert.False((await pipe.ReadAsync()).RootElement.TryGetProperty("id", out _));
            await pipe.ReplyAsync(await pipe.ReadAsync(), new
            {
                tools = new object[]
                {
                    new { name = "read_note", annotations = new { readOnlyHint = true }, inputSchema = new { type = "object" } },
                    new { name = "write_note", inputSchema = new { type = "object" } },
                },
            });
            using var call = await pipe.ReadAsync();
            Assert.Equal("write_note", call.RootElement.GetProperty("params").GetProperty("name").GetString());
            await pipe.ReplyAsync(call, new { content = new[] { new { type = "text", text = "saved" } } });
        });

        var tools = await connector.ListToolsAsync(CancellationToken.None);
        Assert.Equal(["read_note", "write_note"], tools.Select(t => t.Name));
        Assert.True(tools[0].ReadOnly);
        var result = await connector.CallToolAsync("write_note", JsonSerializer.SerializeToElement(new { text = "hi" }), CancellationToken.None);
        await server;
        Assert.Equal(new RemoteMcpCallResult(false, "saved"), result);
        Assert.Equal(["initialize", "notifications/initialized", "tools/list", "tools/call"], pipe.Methods);
    }

    [Fact]
    public async Task SkipsNonJsonLinesAndAnswersPing()
    {
        await using var pipe = await FakeServer.StartAsync();
        using var connector = new StdioMcpConnector("local", "Local", pipe.Client, pipe.Client, Quick);
        var server = Task.Run(async () =>
        {
            using var initialize = await pipe.ReadAsync();
            await pipe.WriteLineAsync("starting…");
            await pipe.WriteLineAsync("[1,2]");
            await pipe.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/message\",\"params\":{}}");
            await pipe.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":\"s1\",\"method\":\"ping\"}");
            Assert.Equal("{\"jsonrpc\":\"2.0\",\"id\":\"s1\",\"result\":{}}", await pipe.ReadRawAsync());
            await pipe.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"roots/list\"}");
            using (var refused = JsonDocument.Parse(await pipe.ReadRawAsync()))
            {
                Assert.Equal(1, refused.RootElement.GetProperty("id").GetInt32());
                Assert.Equal(-32601, refused.RootElement.GetProperty("error").GetProperty("code").GetInt32());
            }
            await pipe.ReplyAsync(initialize, new { protocolVersion = "2025-06-18", capabilities = new { } });
            using var initialized = await pipe.ReadAsync();
            await pipe.ReplyAsync(await pipe.ReadAsync(), new { tools = new object[] { new { name = "one" } } });
        });

        Assert.Equal("one", Assert.Single(await connector.ListToolsAsync(CancellationToken.None)).Name);
        await server;
    }

    [Fact]
    public async Task OversizedLineFailsTheRequest()
    {
        await using var pipe = await FakeServer.StartAsync();
        using var connector = new StdioMcpConnector("local", "Local", pipe.Client, pipe.Client, Quick);
        var server = Task.Run(async () =>
        {
            await pipe.ReadAsync();
            await pipe.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"pad\":\"" + new string('x', StdioMcpConnector.MaxResponseBytes) + "\"}}");
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => connector.ListToolsAsync(CancellationToken.None));
        await server;
    }

    [Fact]
    public async Task TimesOut()
    {
        await using var pipe = await FakeServer.StartAsync();
        using var connector = new StdioMcpConnector("local", "Local", pipe.Client, pipe.Client, TimeSpan.FromSeconds(2));
        var reader = Task.Run(pipe.ReadAsync); // reads the request, never answers
        var clock = Stopwatch.StartNew();
        var error = await Record.ExceptionAsync(() => connector.ListToolsAsync(CancellationToken.None));
        Assert.True(error is TimeoutException or OperationCanceledException, error?.GetType().Name);
        Assert.InRange(clock.Elapsed, TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(8));
        (await reader).Dispose();
        Assert.Equal("initialize", Assert.Single(pipe.Methods));

        // A server that never even reads its input blocks the write; the same limit still applies.
        await using var deaf = await FakeServer.StartAsync();
        using var stuck = new StdioMcpConnector("local", "Local", deaf.Client, deaf.Client, TimeSpan.FromSeconds(1));
        clock.Restart();
        await Assert.ThrowsAsync<TimeoutException>(() => stuck.CallToolAsync("big", JsonSerializer.SerializeToElement(new { pad = new string('x', 200_000) }), CancellationToken.None));
        Assert.InRange(clock.Elapsed, TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(8));
    }

    [Fact]
    public async Task ExitBeforeReplyFails()
    {
        using var connector = new StdioMcpConnector("ps", "PowerShell", PowerShell("exit 3"));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => connector.ListToolsAsync(CancellationToken.None));
        Assert.Contains("stopped", error.Message);
    }

    [Fact]
    public async Task ProcessGetsEnvironmentAndSpeaksLines()
    {
        // A one-line "server" that answers the first request with an error carrying an environment value.
        var script = "$r=[Console]::In.ReadLine()|ConvertFrom-Json; [Console]::Out.WriteLine((@{jsonrpc='2.0';id=$r.id;error=@{code=1;message=$env:OC_STDIO_TEST}}|ConvertTo-Json -Compress))";
        var secret = PowerShell(script) with { Environment = new Dictionary<string, string> { ["OC_STDIO_TEST"] = "hello from env" } };
        using var connector = new StdioMcpConnector("ps", "PowerShell", secret);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => connector.ListToolsAsync(CancellationToken.None));
        Assert.Contains("hello from env", error.Message);
    }

    [Fact]
    public async Task MissingCommandFails()
    {
        Assert.Null(StdioMcpConnector.Launch("no-such-tool-xyz", [], Environment.GetEnvironmentVariable("PATH"), null, out var error));
        Assert.Equal("OpenClicky couldn't find no-such-tool-xyz. Check the command, or give its full path.", error);
        using var connector = new StdioMcpConnector("x", "X", new ConnectorSecret("", null, null) { Command = "no-such-tool-xyz" });
        Assert.Equal(error, (await Assert.ThrowsAsync<InvalidOperationException>(() => connector.ListToolsAsync(CancellationToken.None))).Message);
    }

    [Fact]
    public void CmdLauncherRefusesMetacharacters()
    {
        var folder = Path.Combine(Path.GetTempPath(), "openclicky-cmd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var launcher = Path.Combine(folder, "mytool.cmd");
            File.WriteAllText(launcher, "@echo off\r\n");
            foreach (var bad in new[] { "a&b", "a\"b", "%PATH%", "a^b", "a|b", "a<b", "a>b" })
            {
                Assert.Null(StdioMcpConnector.Launch("mytool", [bad], folder, ".EXE;.CMD", out var error));
                Assert.Equal("Arguments for .cmd launchers can't contain \" % ^ & | < or >.", error);
            }
            var launch = StdioMcpConnector.Launch("mytool", ["--port", "3000"], folder, ".EXE;.CMD", out _);
            Assert.NotNull(launch);
            Assert.EndsWith("cmd.exe", launch.Value.FileName, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith("/d /s /c \"", launch.Value.Arguments);
            Assert.Equal($"/d /s /c \"\"{launcher}\" \"--port\" \"3000\"\"", launch.Value.Arguments);

            // A full path is used as is.
            Assert.Equal(launch, StdioMcpConnector.Launch(launcher, ["--port", "3000"], null, null, out _));

            // Trailing backslashes are doubled before the closing quote so the forwarded program still reads C:\Notes\;
            // inner backslashes stay as typed.
            var vault = StdioMcpConnector.Launch("mytool", [@"C:\Notes\", @"C:\a\b", @"x\\"], folder, ".CMD", out _);
            Assert.Equal($"/d /s /c \"\"{launcher}\" \"C:\\Notes\\\\\" \"C:\\a\\b\" \"x\\\\\\\\\"\"", vault!.Value.Arguments);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void SearchesPathOnlyAndRefusesRelativeFolders()
    {
        var folder = Path.Combine(Path.GetTempPath(), "openclicky-cwd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "planted.cmd"), "@echo off\r\n");
            Assert.NotNull(StdioMcpConnector.Launch("planted", [], folder, ".CMD", out _));
            Assert.Null(StdioMcpConnector.Launch("planted", [], "", ".CMD", out var missing));
            Assert.Equal("OpenClicky couldn't find planted. Check the command, or give its full path.", missing);

            foreach (var relative in new[] { @".\planted.cmd", "./planted.cmd", @"sub\planted.cmd", @"C:planted.cmd", @"\planted.cmd" })
            {
                Assert.Null(StdioMcpConnector.Launch(relative, [], folder, ".CMD", out var error));
                Assert.Equal("Give the program's full path, like C:\\Tools\\server.exe, or just its name.", error);
            }
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void ExeArgumentsAreQuotedLikeArgumentList()
    {
        var launch = StdioMcpConnector.Launch("powershell.exe", ["-Command", "a b", "", "x\"y", "end\\"], Environment.GetEnvironmentVariable("PATH"), null, out var error);
        Assert.NotNull(launch);
        Assert.Equal("", error);
        Assert.EndsWith("powershell.exe", launch.Value.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.True(Path.IsPathRooted(launch.Value.FileName));
        Assert.Equal("-Command \"a b\" \"\" \"x\\\"y\" end\\", launch.Value.Arguments);
    }

    [Fact]
    public void DisposeKillsProcess()
    {
        var connector = new StdioMcpConnector("ps", "PowerShell", PowerShell("Start-Sleep -Seconds 60"));
        int? pid = null;
        try
        {
            connector.Start();
            pid = connector.ProcessId;
            Assert.NotNull(pid);
            connector.Dispose();
            connector.Dispose();
            Assert.True(Exited(pid.Value), "the server process is still running");
        }
        finally
        {
            connector.Dispose();
            if (pid is { } id && !Exited(id)) try { Process.GetProcessById(id).Kill(entireProcessTree: true); } catch (Exception) { }
        }
    }

    private static bool Exited(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.WaitForExit(5_000);
        }
        catch (ArgumentException) { return true; }
    }

    private static ConnectorSecret PowerShell(string command) =>
        new("", null, null) { Command = "powershell.exe", Arguments = ["-NoProfile", "-NonInteractive", "-Command", command] };

    /// <summary>The far end of a duplex in-memory pipe that plays a local MCP server.</summary>
    private sealed class FakeServer : IAsyncDisposable
    {
        private readonly NamedPipeServerStream _server;
        private readonly StreamReader _reader;
        private readonly StreamWriter _writer;

        private FakeServer(NamedPipeServerStream server, NamedPipeClientStream client)
        {
            _server = server;
            Client = client;
            _reader = new StreamReader(server, new UTF8Encoding(false));
            _writer = new StreamWriter(server, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        }

        public NamedPipeClientStream Client { get; }
        public List<string> Methods { get; } = [];

        public static async Task<FakeServer> StartAsync()
        {
            var name = "openclicky-stdio-test-" + Guid.NewGuid().ToString("N");
            var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            await Task.WhenAll(server.WaitForConnectionAsync(), client.ConnectAsync(5_000));
            return new FakeServer(server, client);
        }

        public async Task<string> ReadRawAsync() =>
            await _reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)) ?? throw new EndOfStreamException();

        /// <summary>Reads one client message; each line must be exactly one JSON object.</summary>
        public async Task<JsonDocument> ReadAsync()
        {
            var document = JsonDocument.Parse(await ReadRawAsync());
            Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
            Methods.Add(document.RootElement.GetProperty("method").GetString()!);
            return document;
        }

        public Task WriteLineAsync(string line) => _writer.WriteLineAsync(line);

        public Task ReplyAsync(JsonDocument request, object result) =>
            WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = request.RootElement.GetProperty("id").GetInt32(), result }));

        public async ValueTask DisposeAsync()
        {
            await _server.DisposeAsync();
            await Client.DisposeAsync();
        }
    }
}
