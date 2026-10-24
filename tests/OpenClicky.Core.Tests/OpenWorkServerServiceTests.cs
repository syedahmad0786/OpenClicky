using System.Net;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using OpenClicky.Providers;

namespace OpenClicky.Core.Tests;

public sealed class OpenWorkServerServiceTests
{
    [Fact]
    public void BootstrapHttpTimeout_AllowsBoundedColdEngineReload()
    {
        Assert.Equal(TimeSpan.FromSeconds(90), OpenWorkServerService.BootstrapHttpTimeout);
        Assert.True(OpenWorkServerService.BootstrapHttpTimeout > TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Launch_IsPrivateIsolatedManualAndManaged()
    {
        var root = Path.Combine(Path.GetTempPath(), "ow-test-state");
        var profile = Profile();
        var start = OpenWorkServerService.CreateStartInfo(
            @"C:\runtime\node.exe", @"C:\runtime\openwork-server.mjs", @"C:\runtime\opencode.exe",
            @"D:\Workspace", root, 43123, "client-secret", "host-secret", profile.ToConfigJson());

        Assert.Equal(@"C:\runtime\node.exe", start.FileName);
        Assert.Equal(
            [@"C:\runtime\openwork-server.mjs", "--host", "127.0.0.1", "--port", "43123", "--approval", "manual", "--workspace", @"D:\Workspace"],
            start.ArgumentList);
        Assert.False(start.UseShellExecute);
        Assert.True(start.CreateNoWindow);
        Assert.Equal("1", start.Environment["OPENWORK_MANAGE_OPENCODE"]);
        Assert.Equal(@"C:\runtime\opencode.exe", start.Environment["OPENWORK_OPENCODE_BIN"]);
        Assert.Equal("1", start.Environment["OPENWORK_NO_UPDATE_CHECK"]);
        Assert.Equal("0", start.Environment["OPENWORK_WEB_BOOTSTRAP_TOKEN"]);
        Assert.Equal("client-secret", start.Environment["OPENWORK_TOKEN"]);
        Assert.Equal("host-secret", start.Environment["OPENWORK_HOST_TOKEN"]);
        Assert.Contains("\"*\":\"deny\"", start.Environment["OPENCODE_CONFIG_CONTENT"]);
        Assert.StartsWith(root, start.Environment["OPENWORK_SERVER_CONFIG"], StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(root, start.Environment["XDG_CONFIG_HOME"], StringComparison.OrdinalIgnoreCase);
        Assert.False(start.Environment.ContainsKey("OPENCODE_CONFIG"));
        Assert.False(start.Environment.ContainsKey("OPENAI_API_KEY"));
        Assert.False(start.Environment.ContainsKey("ANTHROPIC_API_KEY"));
        Assert.DoesNotContain("client-secret", start.ArgumentList);
        Assert.DoesNotContain("host-secret", start.ArgumentList);
    }

    [Fact]
    public void WorkspaceDiscovery_RequiresExactRequestedPath()
    {
        var expected = Path.GetFullPath(@"D:\Workspace");
        var json = $$"""{"workspaces":[{"id":"ws_other","path":"D:\\Other"},{"id":"ws_exact","path":{{System.Text.Json.JsonSerializer.Serialize(expected)}}}]}""";

        Assert.Equal("ws_exact", OpenWorkServerService.ParseWorkspaceId(json, expected));
        Assert.Throws<InvalidDataException>(() => OpenWorkServerService.ParseWorkspaceId("{\"workspaces\":[]}", expected));
        Assert.Throws<InvalidDataException>(() => OpenWorkServerService.ParseWorkspaceId("{\"workspaces\":{}}", expected));
    }

    [Fact]
    public void PluginDependencySeed_ExtractsOnlyValidatedPrivateFiles()
    {
        var root = TempDirectory();
        try
        {
            var archive = Path.Combine(root, "seed.tgz");
            WriteArchive(archive,
                ("package.json", TarEntryType.RegularFile, "{}"),
                ("package-lock.json", TarEntryType.RegularFile, "{}"),
                ("node_modules/example/index.js", TarEntryType.RegularFile, "export {};"));
            var target = Path.Combine(root, "private", "config", "opencode");

            OpenWorkServerService.ExtractPluginDependencyArchive(archive, target, Hash(archive));

            Assert.Equal("export {};", File.ReadAllText(Path.Combine(target, "node_modules", "example", "index.js")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void PluginDependencySeed_MaterializesNpmBinAliasesAsRegularFiles()
    {
        var root = TempDirectory();
        try
        {
            var archive = Path.Combine(root, "aliases.tgz");
            var aliases = new[]
            {
                ("download-msgpackr-prebuilds", "../msgpackr-extract/bin/download-prebuilds.js"),
                ("node-which", "../which/bin/node-which"),
                ("uuid", "../uuid/dist-node/bin/uuid"),
                ("node-gyp-build", "../node-gyp-build-optional-packages/bin.js"),
                ("node-gyp-build-optional", "../node-gyp-build-optional-packages/optional.js"),
                ("node-gyp-build-test", "../node-gyp-build-optional-packages/build-test.js"),
                ("yaml", "../yaml/bin.mjs"),
            };
            var entries = new List<(string Name, TarEntryType Type, string Content)>
            {
                ("package.json", TarEntryType.RegularFile, "{}"),
                ("package-lock.json", TarEntryType.RegularFile, "{}"),
                ("node_modules/", TarEntryType.Directory, ""),
                ("node_modules/.bin/", TarEntryType.Directory, ""),
            };
            foreach (var (name, link) in aliases)
            {
                var target = Path.GetFullPath(Path.Combine("C:\\node_modules\\.bin", link.Replace('/', '\\')))
                    .Substring("C:\\node_modules\\".Length).Replace('\\', '/');
                entries.Add(($"node_modules/{target}", TarEntryType.RegularFile, $"tool:{name}"));
                entries.Add(($"node_modules/.bin/{name}", TarEntryType.SymbolicLink, link));
            }
            WriteArchive(archive, entries.ToArray());
            var targetRoot = Path.Combine(root, "target");

            OpenWorkServerService.ExtractPluginDependencyArchive(archive, targetRoot, Hash(archive));

            foreach (var (name, _) in aliases)
            {
                var alias = Path.Combine(targetRoot, "node_modules", ".bin", name);
                Assert.Equal($"tool:{name}", File.ReadAllText(alias));
                Assert.Equal(0, (int)(File.GetAttributes(alias) & FileAttributes.ReparsePoint));
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("../../outside")]
    [InlineData("/absolute")]
    [InlineData("C:/absolute")]
    public void PluginDependencySeed_RejectsUnsafeNpmBinAliasTargets(string linkTarget)
    {
        var root = TempDirectory();
        try
        {
            var archive = Path.Combine(root, "bad-alias.tgz");
            WriteArchive(archive,
                ("package.json", TarEntryType.RegularFile, "{}"),
                ("package-lock.json", TarEntryType.RegularFile, "{}"),
                ("node_modules/example/tool.js", TarEntryType.RegularFile, "tool"),
                ("node_modules/.bin/tool", TarEntryType.SymbolicLink, linkTarget));
            Assert.Throws<InvalidDataException>(() => OpenWorkServerService.ExtractPluginDependencyArchive(
                archive, Path.Combine(root, "target"), Hash(archive)));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void PluginDependencySeed_RejectsHardLinksAndHonorsCancellation()
    {
        var root = TempDirectory();
        try
        {
            var archive = Path.Combine(root, "hardlink.tgz");
            WriteArchive(archive,
                ("package.json", TarEntryType.RegularFile, "{}"),
                ("package-lock.json", TarEntryType.RegularFile, "{}"),
                ("node_modules/example/tool.js", TarEntryType.RegularFile, "tool"),
                ("node_modules/.bin/tool", TarEntryType.HardLink, "../example/tool.js"));
            Assert.Throws<InvalidDataException>(() => OpenWorkServerService.ExtractPluginDependencyArchive(
                archive, Path.Combine(root, "hard-target"), Hash(archive)));
            Assert.Throws<OperationCanceledException>(() => OpenWorkServerService.ExtractPluginDependencyArchive(
                archive, Path.Combine(root, "cancel-target"), Hash(archive), new CancellationToken(canceled: true)));
            Assert.False(Directory.Exists(Path.Combine(root, "cancel-target")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("../escape.txt", false)]
    [InlineData("/absolute.txt", false)]
    [InlineData("node_modules/CON", false)]
    [InlineData("node_modules/link", true)]
    public void PluginDependencySeed_RejectsTraversalAbsolutePathsAndLinks(string maliciousName, bool link)
    {
        var root = TempDirectory();
        try
        {
            var archive = Path.Combine(root, "bad.tgz");
            WriteArchive(archive,
                ("package.json", TarEntryType.RegularFile, "{}"),
                ("package-lock.json", TarEntryType.RegularFile, "{}"),
                ("node_modules/", TarEntryType.Directory, ""),
                (maliciousName, link ? TarEntryType.SymbolicLink : TarEntryType.RegularFile, "bad"));

            Assert.Throws<InvalidDataException>(() => OpenWorkServerService.ExtractPluginDependencyArchive(
                archive, Path.Combine(root, "target"), Hash(archive)));
            Assert.False(File.Exists(Path.Combine(root, "escape.txt")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void PluginDependencySeed_RejectsWrongVersionAndMissingAsset()
    {
        var root = TempDirectory();
        try
        {
            var package = Path.Combine(root, "openwork-server");
            var bin = Directory.CreateDirectory(Path.Combine(package, "bin")).FullName;
            var dist = Directory.CreateDirectory(Path.Combine(package, "dist")).FullName;
            var server = Path.Combine(bin, "openwork-server.mjs");
            File.WriteAllText(server, "fixture");

            Assert.Throws<FileNotFoundException>(() => OpenWorkServerService.SeedBundledPluginDependencies(server, Path.Combine(root, "state-a")));
            File.WriteAllText(Path.Combine(dist, "opencode-plugin-deps-1.18.29.tgz"), "fixture");
            Assert.Throws<NotSupportedException>(() => OpenWorkServerService.SeedBundledPluginDependencies(server, Path.Combine(root, "state-b")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task OpenWorkFactory_UsesWorkspaceProxyAndBearerOnly()
    {
        using var http = new HttpClient(new Handler((request, _) =>
        {
            Assert.Equal("/workspace/ws_123/opencode/global/health", request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("ephemeral-token", request.Headers.Authorization.Parameter);
            return Task.FromResult(Json("{\"healthy\":true,\"version\":\"1.18.30\"}"));
        }));
        using var client = OpenCodeTaskClient.ForOpenWorkWorkspace(
            "http://127.0.0.1:8787/", "ws_123", "ephemeral-token", http);

        var health = await client.GetHealthAsync();

        Assert.True(health.Healthy);
    }

    [Fact]
    public void EffectiveProfile_FailsClosedWithoutDenyWildcardOrOwnedProvider()
    {
        const string valid = "{\"model\":\"openclicky-chatgpt/gpt-6-luna\",\"enabled_providers\":[\"openclicky-chatgpt\"],\"permission\":{\"*\":\"deny\",\"openclicky_*\":\"allow\"},\"mcp\":{\"openclicky\":{\"url\":\"http://127.0.0.1:42002/mcp/\",\"headers\":{\"Authorization\":\"Bearer token\"}}}}";
        OpenWorkServerService.EnsureEffectiveProfile(valid);

        Assert.Throws<InvalidOperationException>(() => OpenWorkServerService.EnsureEffectiveProfile(
            valid.Replace("\"*\":\"deny\"", "\"*\":\"allow\"")));
        Assert.Throws<InvalidOperationException>(() => OpenWorkServerService.EnsureEffectiveProfile(
            valid.Replace("openclicky-chatgpt/gpt-6-luna", "other/model")));
        Assert.Throws<InvalidOperationException>(() => OpenWorkServerService.EnsureEffectiveProfile(
            valid.Replace("http://127.0.0.1:42002/mcp/", "https://example.com/mcp")));
    }

    [Fact]
    public void EffectiveProfile_RejectsExtraCapabilitiesAndEndpointChanges()
    {
        var expected = Profile().ToConfigJson();
        OpenWorkServerService.EnsureEffectiveProfile(expected, expected);

        Assert.Throws<InvalidOperationException>(() => OpenWorkServerService.EnsureEffectiveProfile(
            expected.Replace("\"openclicky_*\":\"allow\"", "\"openclicky_*\":\"allow\",\"bash\":\"allow\""), expected));
        Assert.Throws<InvalidOperationException>(() => OpenWorkServerService.EnsureEffectiveProfile(
            expected.Replace("\"mcp\":{", "\"mcp\":{\"ambient\":{\"type\":\"local\",\"command\":[\"cmd\"]},"), expected));
        Assert.Throws<InvalidOperationException>(() => OpenWorkServerService.EnsureEffectiveProfile(
            expected.Replace("http://127.0.0.1:42001/v1", "http://127.0.0.1:49999/v1"), expected));
    }

    [Fact]
    public async Task BootstrapProfile_MatchingEffectiveProfileSkipsReload()
    {
        var expected = Profile().ToConfigJson();
        var requests = new List<(HttpMethod Method, string Path)>();
        using var http = new HttpClient(new Handler((request, _) =>
        {
            requests.Add((request.Method, request.RequestUri!.AbsolutePath));
            return Task.FromResult(Json(expected));
        }));

        await OpenWorkServerService.EnsureProfileAppliedAsync(
            http, new("http://127.0.0.1:8787/"), "ws_123", "token", expected, CancellationToken.None);

        Assert.Equal([(HttpMethod.Get, "/workspace/ws_123/opencode/config")], requests);
    }

    [Fact]
    public async Task BootstrapProfile_MismatchReloadsAndRequiresVerifiedResult()
    {
        var expected = Profile().ToConfigJson();
        var responses = new Queue<HttpResponseMessage>([
            Json(expected.Replace("gpt-6-luna", "other-model")),
            Json("{\"ok\":true}"),
            Json(expected),
        ]);
        var requests = new List<(HttpMethod Method, string Path)>();
        using var http = new HttpClient(new Handler((request, _) =>
        {
            requests.Add((request.Method, request.RequestUri!.AbsolutePath));
            return Task.FromResult(responses.Dequeue());
        }));

        await OpenWorkServerService.EnsureProfileAppliedAsync(
            http, new("http://127.0.0.1:8787/"), "ws_123", "token", expected, CancellationToken.None);

        Assert.Equal([
            (HttpMethod.Get, "/workspace/ws_123/opencode/config"),
            (HttpMethod.Post, "/workspace/ws_123/engine/reload"),
            (HttpMethod.Get, "/workspace/ws_123/opencode/config"),
        ], requests);
    }

    [Fact]
    public async Task BootstrapProgress_ReportsSanitizedStageOrderingAndElapsedTime()
    {
        var expected = Profile().ToConfigJson();
        var responses = new Queue<HttpResponseMessage>([
            Json(expected.Replace("gpt-6-luna", "other-model")), Json("{\"ok\":true}"), Json(expected),
        ]);
        var progress = new List<(string Stage, bool Completed, long Milliseconds)>();
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(responses.Dequeue())));

        await OpenWorkServerService.EnsureProfileAppliedAsync(
            http, new("http://127.0.0.1:8787/"), "ws_123", "token", expected, CancellationToken.None,
            (stage, completed, milliseconds) => progress.Add((stage, completed, milliseconds)));

        Assert.Equal([
            ("effective-profile", false), ("effective-profile", true),
            ("engine-reload", false), ("engine-reload", true),
            ("effective-profile", false), ("effective-profile", true),
        ], progress.Select(item => (item.Stage, item.Completed)));
        Assert.All(progress.Where(item => item.Completed), item => Assert.True(item.Milliseconds >= 0));
        Assert.All(progress.Where(item => !item.Completed), item => Assert.Equal(0, item.Milliseconds));
    }

    [Fact]
    public async Task BootstrapProgress_OnErrorReportsStartOnlyAndPreservesFailure()
    {
        var progress = new List<(string Stage, bool Completed, long Milliseconds)>();
        using var http = new HttpClient(new Handler((_, _) => throw new HttpRequestException("synthetic transport failure")));

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => OpenWorkServerService.EnsureProfileAppliedAsync(
            http, new("http://127.0.0.1:8787/"), "ws_123", "token", Profile().ToConfigJson(), CancellationToken.None,
            (stage, completed, milliseconds) => progress.Add((stage, completed, milliseconds))));

        Assert.Equal("synthetic transport failure", error.Message);
        Assert.Equal([("effective-profile", false, 0L)], progress);
    }

    [Fact]
    public async Task BootstrapProfile_UnsafeAfterReloadFailsClosed()
    {
        var expected = Profile().ToConfigJson();
        var unsafeProfile = expected.Replace("\"*\":\"deny\"", "\"*\":\"allow\"");
        var responses = new Queue<HttpResponseMessage>([Json(unsafeProfile), Json("{\"ok\":true}"), Json(unsafeProfile)]);
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(responses.Dequeue())));

        await Assert.ThrowsAsync<InvalidOperationException>(() => OpenWorkServerService.EnsureProfileAppliedAsync(
            http, new("http://127.0.0.1:8787/"), "ws_123", "token", expected, CancellationToken.None));
    }

    [Fact]
    public async Task BootstrapProfile_MalformedResponseFailsWithoutReload()
    {
        var expected = Profile().ToConfigJson();
        var requests = 0;
        using var http = new HttpClient(new Handler((_, _) =>
        {
            requests++;
            return Task.FromResult(Json("{not-json"));
        }));

        await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(() => OpenWorkServerService.EnsureProfileAppliedAsync(
            http, new("http://127.0.0.1:8787/"), "ws_123", "token", expected, CancellationToken.None));
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task BootstrapProfile_TransportFailureFailsWithoutReload()
    {
        var expected = Profile().ToConfigJson();
        var requests = 0;
        using var http = new HttpClient(new Handler((_, _) =>
        {
            requests++;
            throw new HttpRequestException("synthetic transport failure");
        }));

        await Assert.ThrowsAsync<HttpRequestException>(() => OpenWorkServerService.EnsureProfileAppliedAsync(
            http, new("http://127.0.0.1:8787/"), "ws_123", "token", expected, CancellationToken.None));
        Assert.Equal(1, requests);
    }

    [Theory]
    [InlineData("../other")]
    [InlineData("ws/other")]
    [InlineData("")]
    public void OpenWorkFactory_RejectsInvalidWorkspaceIds(string workspaceId)
    {
        Assert.ThrowsAny<ArgumentException>(() => OpenCodeTaskClient.ForOpenWorkWorkspace(
            "http://127.0.0.1:8787/", workspaceId, "token"));
    }

    private static OpenCodeCompanionConfiguration Profile() => new(
        new("http://127.0.0.1:42001/"), "inference-token",
        new("http://127.0.0.1:42002/mcp/"), "tool-token", "gpt-6-luna");

    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "OpenClicky-seed-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static void WriteArchive(string path, params (string Name, TarEntryType Type, string Content)[] entries)
    {
        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.SmallestSize);
        using var writer = new TarWriter(gzip, leaveOpen: false);
        foreach (var item in entries)
        {
            var entry = new PaxTarEntry(item.Type, item.Name);
            if (item.Type is TarEntryType.SymbolicLink or TarEntryType.HardLink) entry.LinkName = item.Content;
            else if (item.Type != TarEntryType.Directory) entry.DataStream = new MemoryStream(Encoding.UTF8.GetBytes(item.Content));
            writer.WriteEntry(entry);
        }
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
}
