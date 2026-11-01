using System.IO;
using System.Net.Http;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using OpenClicky.Core;
using OpenClicky.Core.Settings;
using OpenClicky.Core.Shell;
using OpenClicky.Providers;

/// <summary>
/// Native acceptance for sub-project F, all on loopback and in a temp folder: no real browser, account or third-party host.
/// Run from the built exe: <c>OpenClicky.App.SecretStore.Smoke.exe --connectors</c>. One PASS/FAIL line per check; exit 0 only when all pass.
/// </summary>
internal static class ConnectorsCheck
{
    public static async Task<int> RunAsync()
    {
        var failures = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        async Task Check(string name, Func<Task<string>> body)
        {
            try { Console.WriteLine($"PASS {name}: {await body()}"); }
            catch (Exception ex) { failures++; Console.WriteLine($"FAIL {name}: {ex.GetType().Name}: {ex.Message}"); }
        }

        await Check("oauth-sign-in-verify-refresh", () => OAuthAsync(cts.Token));
        await Check("real-secret-store-round-trip", () => Task.FromResult(SecretStoreRoundTrip()));
        await Check("stdio-powershell-server", () => StdioAsync(cts.Token));
        await Check("obsidian-vault", () => ObsidianAsync(cts.Token));

        Console.WriteLine(failures == 0 ? "PASS all connector checks" : $"FAIL {failures} connector check(s)");
        return failures == 0 ? 0 : 1;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    // ---- 1. browser sign-in, verify and refresh against a loopback fake MCP + authorization server -------------------

    private static async Task<string> OAuthAsync(CancellationToken ct)
    {
        using var server = new FakeMcpServer(FreePort());
        var mcp = new Uri(server.McpUrl);
        var callbackPort = FreePort();
        using var oauth = new McpOAuth(null, async url =>
        {
            // A harness for our own fake server, not a sign-in: it never leaves 127.0.0.1.
            Require(url.IsLoopback, "The authorize URL was not on this PC.");
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
            using var authorize = await http.GetAsync(url, ct);
            Require((int)authorize.StatusCode == 302 && authorize.Headers.Location is { IsLoopback: true } callback, "The authorize endpoint did not redirect to the loopback callback.");
            using var delivered = await http.GetAsync(authorize.Headers.Location, ct);
        }, callbackPort);

        var tokens = await oauth.SignInAsync(mcp, null, ct);
        Require(tokens.AccessToken == server.CurrentAccess && tokens.RefreshToken is { Length: > 0 }, "Sign-in did not return the issued tokens.");
        Require(tokens.ClientId == "smoke-client" && server.RegisterCalls == 1, "Dynamic client registration did not run exactly once.");
        Require(tokens.Resource == server.McpUrl, "The token is not bound to the MCP resource.");
        Require(server.PkceVerified, "The server did not see a matching PKCE S256 verifier.");
        Require(server.AuthorizeResource == server.McpUrl && server.AuthorizeRedirect == McpOAuth.RedirectUri(callbackPort), "The authorize request lacked the resource or the loopback redirect.");

        var saved = tokens;
        var source = new McpTokenSource(tokens, oauth, t => saved = t);
        using var first = new RemoteMcpConnector("smoke", "Smoke", new ConnectorSecret(server.McpUrl, null, null), null, source.GetAsync);
        var check = await ConnectorVerifier.CheckAsync(first, ConnectorAuth.Browser, TimeSpan.FromSeconds(30), ct);
        Require(check.Status == ConnectorStatus.Connected && check.ToolCount == FakeMcpServer.ToolCount,
            $"Verify gave {check.Status} with {check.ToolCount?.ToString() ?? "no"} tools, not Connected with {FakeMcpServer.ToolCount}.");
        Require(server.ListBearers.Count == 1 && server.ListBearers[0] == tokens.AccessToken && server.RefreshCalls == 0, "The first check should use the signed-in token without refreshing.");

        // Forced refresh: pretend two hours passed until the refresh has happened once.
        var forced = true;
        var later = DateTimeOffset.UtcNow.AddHours(2);
        var refreshing = new McpTokenSource(saved, oauth, t => { saved = t; forced = false; }, () => forced ? later : DateTimeOffset.UtcNow);
        using var second = new RemoteMcpConnector("smoke", "Smoke", new ConnectorSecret(server.McpUrl, null, null), null, refreshing.GetAsync);
        var afterRefresh = await ConnectorVerifier.CheckAsync(second, ConnectorAuth.Browser, TimeSpan.FromSeconds(30), ct);
        Require(afterRefresh.Status == ConnectorStatus.Connected && afterRefresh.ToolCount == FakeMcpServer.ToolCount, $"After refresh the check gave {afterRefresh.Status}.");
        Require(server.RefreshCalls == 1 && saved.AccessToken != tokens.AccessToken && saved.RefreshToken != tokens.RefreshToken,
            "The refresh did not run once and rotate both tokens.");
        Require(server.ListBearers[^1] == saved.AccessToken, "tools/list after refresh did not use the new token.");

        // A refused refresh must read as "Reconnect required", not a crash.
        server.RefuseRefresh = true;
        var rejectedNow = DateTimeOffset.UtcNow.AddHours(2);
        var doomed = new McpTokenSource(saved, oauth, _ => { }, () => rejectedNow);
        using var third = new RemoteMcpConnector("smoke", "Smoke", new ConnectorSecret(server.McpUrl, null, null), null, doomed.GetAsync);
        var rejected = await ConnectorVerifier.CheckAsync(third, ConnectorAuth.Browser, TimeSpan.FromSeconds(30), ct);
        Require(rejected.Status == ConnectorStatus.Reconnect, $"A refused refresh gave {rejected.Status}, not Reconnect.");

        return $"sign-in via discovery, registration, PKCE and resource; Connected with {check.ToolCount} tools; refresh rotated the tokens; refused refresh is Reconnect";
    }

    // ---- 2. the real Windows secret store with a registry on a temporary database ------------------------------------

    private static string SecretStoreRoundTrip()
    {
        var folder = Path.Combine(Path.GetTempPath(), "OpenClicky-Connectors-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var store = new OpenClicky.App.WindowsCredentialSecretStore();
        var registry = new ConnectorRegistry(store, Path.Combine(folder, "connectors.db"));
        var name = "smoke-" + Guid.NewGuid().ToString("N")[..8];
        var marker = "SMOKESECRET" + Guid.NewGuid().ToString("N");
        string? key = null;
        try
        {
            var secret = new ConnectorSecret($"http://127.0.0.1:1/mcp?account={marker}-url", null, null)
            {
                Headers = new Dictionary<string, string> { ["X-Smoke"] = marker + "-header" },
                OAuth = new McpOAuthTokens(marker + "-access-" + new string('a', 3_000), marker + "-refresh", DateTimeOffset.UtcNow.AddHours(1),
                    "http://127.0.0.1:1/token", "smoke-client", marker + "-client-secret", "client_secret_basic", "http://127.0.0.1:1/mcp", null),
            };
            Require(Encoding.Unicode.GetByteCount(JsonSerializer.Serialize(secret)) > 2_560, "The test secret is not above the Credential Manager limit.");
            var info = registry.Add(new NewConnector(name, ConnectorKind.Remote, "127.0.0.1", null, null, ConnectorAuth.Browser, secret));
            key = info.Key;
            var dpapi = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenClicky", "secrets", ConnectorRegistry.SecretName(key) + ".dpapi");
            Require(File.Exists(dpapi), "The oversized secret did not go to a DPAPI file.");
            var back = registry.Secret(key) ?? throw new InvalidOperationException("The secret did not read back.");
            Require(back.OAuth?.AccessToken == secret.OAuth!.AccessToken && back.Headers?["X-Smoke"] == marker + "-header" && back.Url == secret.Url && back.OAuth.ClientSecret == secret.OAuth.ClientSecret,
                "The secret did not round-trip intact.");

            // Nothing private may sit in the database (or its journal files).
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var file in Directory.GetFiles(folder))
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                var text = Encoding.UTF8.GetString(memory.ToArray()) + Encoding.Unicode.GetString(memory.ToArray());
                Require(!text.Contains(marker, StringComparison.Ordinal) && !text.Contains("account=", StringComparison.Ordinal), $"A secret string reached {Path.GetFileName(file)}.");
            }
            Require(registry.List().Single().Key == key, "The registry should hold exactly the smoke connector.");

            registry.Remove(key);
            Require(store.Get(ConnectorRegistry.SecretName(key)) is null && !File.Exists(dpapi), "Remove left the secret or its DPAPI file behind.");
            key = null;
            return "over-limit secret went to DPAPI, round-tripped, stayed out of the database, and Remove deleted both copies";
        }
        finally
        {
            if (key is not null) registry.Remove(key);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    // ---- 3. a local command over stdio -------------------------------------------------------------------------------

    private const string StdioServer = """
        Set-Content -Path $env:SMOKE_PID_FILE -Value $PID -Encoding ascii
        while ($null -ne ($line = [Console]::In.ReadLine())) {
          if ($line.Trim().Length -eq 0) { continue }
          $message = $line | ConvertFrom-Json
          if ($null -eq $message.id) { continue }
          if ($message.method -eq 'initialize') {
            $result = @{ protocolVersion = '2025-06-18'; capabilities = @{ tools = @{} }; serverInfo = @{ name = 'smoke'; version = '1' } }
          } elseif ($message.method -eq 'tools/list') {
            $result = @{ tools = @( @{ name = 'echo'; description = 'Echo the input.'; inputSchema = @{ type = 'object' } } ) }
          } else {
            $result = @{}
          }
          [Console]::Out.WriteLine((@{ jsonrpc = '2.0'; id = $message.id; result = $result } | ConvertTo-Json -Depth 8 -Compress))
          [Console]::Out.Flush()
        }
        """;

    private static async Task<string> StdioAsync(CancellationToken ct)
    {
        var folder = Path.Combine(Path.GetTempPath(), "OpenClicky-Stdio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var script = Path.Combine(folder, "server.ps1");
            var pidFile = Path.Combine(folder, "pid.txt");
            await File.WriteAllTextAsync(script, StdioServer, ct);
            var secret = new ConnectorSecret("", null, null)
            {
                Command = "powershell.exe",
                Arguments = ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script],
                Environment = new Dictionary<string, string> { ["SMOKE_PID_FILE"] = pidFile },
            };
            int pid;
            using (var connector = new StdioMcpConnector("smoke_stdio", "Smoke stdio", secret))
            {
                var check = await ConnectorVerifier.CheckAsync(connector, ConnectorAuth.None, TimeSpan.FromSeconds(30), ct);
                Require(check.Status == ConnectorStatus.Connected && check.ToolCount == 1, $"The local command gave {check.Status} with {check.ToolCount?.ToString() ?? "no"} tools.");
                var tools = await connector.ListToolsAsync(ct);
                Require(tools.Count == 1 && tools[0].Name == "echo", "The tool list was not the script's one tool.");
                pid = int.Parse((await File.ReadAllTextAsync(pidFile, ct)).Trim());
                Require(!HasExited(pid), "The server process was not running while connected.");
            }
            for (var wait = 0; wait < 50 && !HasExited(pid); wait++) await Task.Delay(100, ct);
            Require(HasExited(pid), "The server process was still running after Dispose.");
            return "powershell.exe fake server listed 1 tool; the process was gone after Dispose";
        }
        finally { try { Directory.Delete(folder, recursive: true); } catch (IOException) { } }
    }

    private static bool HasExited(int pid)
    {
        try { using var process = Process.GetProcessById(pid); return process.HasExited; }
        catch (ArgumentException) { return true; }
    }

    // ---- 4. an Obsidian vault ----------------------------------------------------------------------------------------

    private static async Task<string> ObsidianAsync(CancellationToken ct)
    {
        var parent = Path.Combine(Path.GetTempPath(), "OpenClicky-Vault-" + Guid.NewGuid().ToString("N"));
        var vault = Path.Combine(parent, "vault");
        Directory.CreateDirectory(Path.Combine(vault, ".obsidian"));
        Directory.CreateDirectory(Path.Combine(vault, "Daily"));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(vault, "Daily", "2026-10-25.md"), "# Today\nBuy oat milk\n", ct);
            await File.WriteAllTextAsync(Path.Combine(vault, "Ideas.md"), "Connector idea: a pineapple graph\n", ct);
            using var connector = new ObsidianVaultConnector(vault);
            var tools = await connector.ListToolsAsync(ct);
            Require(tools.Count == ObsidianVaultConnector.ToolCount, "The vault should offer its four tools.");

            async Task<RemoteMcpCallResult> Call(string tool, object arguments) =>
                await connector.CallToolAsync(tool, JsonSerializer.SerializeToElement(arguments), ct);

            var list = await Call("list_notes", new { });
            Require(!list.IsError && list.Text.Contains("Daily/2026-10-25.md") && list.Text.Contains("Ideas.md") && !list.Text.Contains(".obsidian"), $"list_notes was wrong: {Short(list.Text)}");
            var search = await Call("search_notes", new { query = "pineapple" });
            Require(!search.IsError && search.Text.Contains("Ideas.md") && search.Text.Contains("pineapple"), $"search_notes was wrong: {Short(search.Text)}");
            var read = await Call("read_note", new { path = "Daily/2026-10-25.md" });
            Require(!read.IsError && read.Text.Contains("oat milk"), $"read_note was wrong: {Short(read.Text)}");

            foreach (var escape in new[] { "../escape.md", Path.Combine(parent, "absolute.md"), ".obsidian/app.md", "Daily/../../escape2.md", "notes.txt" })
            {
                var refused = await Call("write_note", new { path = escape, content = "x" });
                Require(refused.IsError, $"write_note accepted '{escape}'.");
            }
            var outside = Directory.GetFiles(parent, "*", SearchOption.TopDirectoryOnly);
            Require(outside.Length == 0, "A refused write left a file outside the vault.");
            Require(!File.Exists(Path.Combine(vault, ".obsidian", "app.md")) && !File.Exists(Path.Combine(vault, "notes.txt")), "A refused write left a file inside the vault.");

            var written = await Call("write_note", new { path = "Inbox/new.md", content = "hello" });
            Require(!written.IsError && File.ReadAllText(Path.Combine(vault, "Inbox", "new.md")) == "hello", "A valid write_note did not create the note.");
            return "list, search and read worked; five escape attempts refused with nothing written outside the vault; a valid write succeeded";
        }
        finally { try { Directory.Delete(parent, recursive: true); } catch (IOException) { } }
    }

    private static string Short(string text) => text.Length <= 120 ? text : text[..120];

    // ---- the fake MCP server + authorization server ------------------------------------------------------------------

    private sealed class FakeMcpServer : IDisposable
    {
        public const int ToolCount = 2;
        private readonly HttpListener _listener = new();
        private readonly string _base;
        private readonly Dictionary<string, (string Challenge, string Redirect)> _codes = new();
        private readonly CancellationTokenSource _stop = new();
        private int _counter;
        private string _refresh = "";

        public FakeMcpServer(int port)
        {
            _base = $"http://127.0.0.1:{port}";
            _listener.Prefixes.Add(_base + "/");
            _listener.Start();
            _ = Task.Run(Loop);
        }

        public string McpUrl => _base + "/mcp";
        public string CurrentAccess { get; private set; } = "";
        public bool RefuseRefresh { get; set; }
        public bool PkceVerified { get; private set; }
        public int RegisterCalls { get; private set; }
        public int RefreshCalls { get; private set; }
        public string? AuthorizeResource { get; private set; }
        public string? AuthorizeRedirect { get; private set; }
        public List<string> ListBearers { get; } = [];

        public void Dispose()
        {
            _stop.Cancel();
            try { _listener.Close(); } catch (Exception) { }
        }

        private async Task Loop()
        {
            while (!_stop.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch (Exception) { return; }
                try { await Handle(context); }
                catch (Exception) { try { context.Response.Abort(); } catch (Exception) { } }
            }
        }

        private async Task Handle(HttpListenerContext context)
        {
            var request = context.Request;
            var path = request.Url!.AbsolutePath;
            if (request.HttpMethod == "GET" && path.StartsWith("/.well-known/oauth-protected-resource", StringComparison.Ordinal))
                await Json(context, new { resource = McpUrl, authorization_servers = new[] { _base }, scopes_supported = new[] { "read" } });
            else if (request.HttpMethod == "GET" && path == "/.well-known/oauth-authorization-server")
                await Json(context, new
                {
                    issuer = _base, authorization_endpoint = _base + "/authorize", token_endpoint = _base + "/token",
                    registration_endpoint = _base + "/register", code_challenge_methods_supported = new[] { "S256" },
                });
            else if (request.HttpMethod == "POST" && path == "/register")
            {
                RegisterCalls++;
                await Json(context, new { client_id = "smoke-client", token_endpoint_auth_method = "none" }, 201);
            }
            else if (request.HttpMethod == "GET" && path == "/authorize") Authorize(context);
            else if (request.HttpMethod == "POST" && path == "/token") await Token(context);
            else if (request.HttpMethod == "POST" && path == "/mcp") await Mcp(context);
            else { context.Response.StatusCode = 404; context.Response.Close(); }
        }

        private void Authorize(HttpListenerContext context)
        {
            var query = context.Request.QueryString;
            var redirect = query["redirect_uri"] ?? "";
            AuthorizeResource = query["resource"];
            AuthorizeRedirect = redirect;
            if (query["code_challenge_method"] != "S256" || string.IsNullOrEmpty(query["code_challenge"]) || string.IsNullOrEmpty(query["state"]) || query["client_id"] != "smoke-client")
            {
                context.Response.StatusCode = 400;
                context.Response.Close();
                return;
            }
            var code = "code-" + Guid.NewGuid().ToString("N");
            _codes[code] = (query["code_challenge"]!, redirect);
            context.Response.StatusCode = 302;
            context.Response.RedirectLocation = redirect + "?code=" + code + "&state=" + Uri.EscapeDataString(query["state"]!);
            context.Response.Close();
        }

        private async Task Token(HttpListenerContext context)
        {
            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
            var form = HttpUtility.ParseQueryString(await reader.ReadToEndAsync());
            if (form["grant_type"] == "authorization_code")
            {
                var code = form["code"] ?? "";
                if (!_codes.Remove(code, out var issued) || form["redirect_uri"] != issued.Redirect || form["resource"] != McpUrl
                    || Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"] ?? ""))) != issued.Challenge)
                {
                    await Json(context, new { error = "invalid_grant" }, 400);
                    return;
                }
                PkceVerified = true;
            }
            else if (form["grant_type"] == "refresh_token")
            {
                RefreshCalls++;
                if (RefuseRefresh || form["refresh_token"] != _refresh)
                {
                    await Json(context, new { error = "invalid_grant" }, 400);
                    return;
                }
            }
            else
            {
                await Json(context, new { error = "unsupported_grant_type" }, 400);
                return;
            }
            _counter++;
            CurrentAccess = "access-" + _counter + "-" + Guid.NewGuid().ToString("N");
            _refresh = "refresh-" + _counter + "-" + Guid.NewGuid().ToString("N");
            await Json(context, new { access_token = CurrentAccess, refresh_token = _refresh, token_type = "Bearer", expires_in = 3600 });
        }

        private async Task Mcp(HttpListenerContext context)
        {
            var bearer = context.Request.Headers["Authorization"];
            if (CurrentAccess.Length == 0 || bearer != "Bearer " + CurrentAccess)
            {
                context.Response.StatusCode = 401;
                context.Response.AddHeader("WWW-Authenticate", $"Bearer resource_metadata=\"{_base}/.well-known/oauth-protected-resource\"");
                context.Response.Close();
                return;
            }
            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
            using var message = JsonDocument.Parse(await reader.ReadToEndAsync());
            var method = message.RootElement.GetProperty("method").GetString();
            if (!message.RootElement.TryGetProperty("id", out var id))
            {
                context.Response.StatusCode = 202;
                context.Response.Close();
                return;
            }
            object result;
            if (method == "initialize")
                result = new { protocolVersion = "2025-06-18", capabilities = new { tools = new { } }, serverInfo = new { name = "fake", version = "1" } };
            else if (method == "tools/list")
            {
                ListBearers.Add(bearer.Substring("Bearer ".Length));
                result = new
                {
                    tools = new[]
                    {
                        new { name = "list_things", description = "List things.", inputSchema = new { type = "object" } },
                        new { name = "get_thing", description = "Get one thing.", inputSchema = new { type = "object" } },
                    },
                };
            }
            else result = new { };
            context.Response.AddHeader("Mcp-Session-Id", "smoke-session");
            await Json(context, new { jsonrpc = "2.0", id = id.Clone(), result });
        }

        private static async Task Json(HttpListenerContext context, object body, int status = 200)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(body);
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }

        private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
