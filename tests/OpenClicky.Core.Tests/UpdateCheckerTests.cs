using System.IO.Compression;
using System.Net;
using System.Text;
using OpenClicky.Core.Updates;

namespace OpenClicky.Core.Tests;

public sealed class UpdateCheckerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "oc-upd-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            var response = respond(request);
            response.RequestMessage ??= request; // HttpClientHandler does this; a redirect test sets its own final request
            return Task.FromResult(response);
        }
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string ReleaseJson(string tag, bool prerelease = false, bool draft = false, string? assets = null) =>
        $$"""
        {"tag_name":"{{tag}}","html_url":"https://github.com/syedahmad0786/OpenClicky/releases/tag/{{tag}}",
         "body":"notes","prerelease":{{(prerelease ? "true" : "false")}},"draft":{{(draft ? "true" : "false")}},
         "assets":[{{assets ?? """
           {"name":"OpenClicky-src.zip","browser_download_url":"https://github.com/x/src.zip","size":5},
           {"name":"OpenClicky-1.2.0-portable.ZIP","browser_download_url":"https://github.com/x/portable.zip","size":1234}
           """}}]}
        """;

    private static UpdateChecker Checker(string json, string current, out FakeHandler handler)
    {
        handler = new FakeHandler(_ => Json(json));
        return new UpdateChecker(new HttpClient(handler), current);
    }

    [Fact]
    public async Task NewerReleaseIsAvailableWithPortableZip()
    {
        var result = await Checker(ReleaseJson("v1.2.0"), "1.1.9", out var handler).CheckAsync(CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal(UpdateStatus.Available, result!.Status);
        Assert.Equal("1.2.0", result.Release.Version);
        Assert.Equal("v1.2.0", result.Release.TagName);
        Assert.Equal("https://github.com/x/portable.zip", result.Release.PortableZipUrl!.ToString());
        Assert.Equal(1234, result.Release.PortableZipSize);
        Assert.Equal("notes", result.Release.Notes);
        var req = Assert.Single(handler.Requests);
        Assert.Equal("https://api.github.com/repos/syedahmad0786/OpenClicky/releases/latest", req.RequestUri!.ToString());
        Assert.Contains(req.Headers.UserAgent, p => p.Product?.Name == "OpenClicky");
        Assert.Contains(req.Headers.Accept, a => a.MediaType == "application/vnd.github+json");
    }

    [Fact]
    public async Task SameVersionIsUpToDateAndVPrefixIgnored()
    {
        var result = await Checker(ReleaseJson("v1.2.0"), "1.2.0", out _).CheckAsync(CancellationToken.None);
        Assert.Equal(UpdateStatus.UpToDate, result!.Status);
        result = await Checker(ReleaseJson("1.2.0"), "V1.2", out _).CheckAsync(CancellationToken.None);
        Assert.Equal(UpdateStatus.UpToDate, result!.Status);
    }

    [Fact]
    public async Task OlderReleaseIsUpToDate()
    {
        var result = await Checker(ReleaseJson("v1.0.0"), "1.2.0", out _).CheckAsync(CancellationToken.None);
        Assert.Equal(UpdateStatus.UpToDate, result!.Status);
    }

    [Theory]
    [InlineData("not json {")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"html_url\":\"https://github.com/x\"}")]
    public async Task MalformedJsonReturnsNull(string body)
    {
        Assert.Null(await Checker(body, "1.0.0", out _).CheckAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://github.com/syedahmad0786/OpenClicky/releases/tag/v2.0.0")]
    [InlineData("https://evil.example/releases/tag/v2.0.0")]
    [InlineData("https://github.com.evil.example/x")]
    [InlineData("javascript:alert(1)")]
    [InlineData("not a url")]
    public async Task UntrustedReleasePageFallsBackToReleasesList(string? htmlUrl)
    {
        var body = htmlUrl is null ? "{\"tag_name\":\"v2.0.0\"}" : $"{{\"tag_name\":\"v2.0.0\",\"html_url\":\"{htmlUrl}\"}}";
        var result = await Checker(body, "1.0.0", out _).CheckAsync(CancellationToken.None);
        Assert.Equal(UpdateStatus.Available, result!.Status);
        Assert.Equal(UpdateChecker.ReleasesPageUrl, result.Release.ReleasePage.AbsoluteUri);
    }

    [Fact]
    public async Task GithubReleasePageIsKept()
    {
        var result = await Checker(ReleaseJson("v1.2.0"), "1.0.0", out _).CheckAsync(CancellationToken.None);
        Assert.Equal("https://github.com/syedahmad0786/OpenClicky/releases/tag/v1.2.0", result!.Release.ReleasePage.AbsoluteUri);
    }

    [Fact]
    public async Task HttpErrorAndExceptionReturnNull()
    {
        var bad = new UpdateChecker(new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden))), "1.0.0");
        Assert.Null(await bad.CheckAsync(CancellationToken.None));
        var boom = new UpdateChecker(new HttpClient(new FakeHandler(_ => throw new HttpRequestException("offline"))), "1.0.0");
        Assert.Null(await boom.CheckAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CancellationPropagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var handler = new CancelAwareHandler();
        var checker = new UpdateChecker(new HttpClient(handler), "1.0.0");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => checker.CheckAsync(cts.Token));
    }

    private sealed class CancelAwareHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Json("{}"));
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task PrereleaseOrDraftIsTreatedAsUpToDateWithCurrentVersion(bool pre, bool draft)
    {
        var result = await Checker(ReleaseJson("v9.0.0", pre, draft), "1.0.0", out _).CheckAsync(CancellationToken.None);
        Assert.Equal(UpdateStatus.UpToDate, result!.Status);
        Assert.Equal("1.0.0", result.Release.Version);
        Assert.Null(result.Release.PortableZipUrl);
    }

    [Theory]
    [InlineData("1.2.0-beta.1", "1.2.0", false)]
    [InlineData("1.2.0", "1.2.0-beta.1", true)]
    [InlineData("1.2.0+build5", "1.2.0", false)]
    [InlineData("1.10.0", "1.9.9", true)]
    [InlineData("2", "1.9.9", true)]
    public async Task SemVerComparison(string latest, string current, bool newer)
    {
        var result = await Checker(ReleaseJson("v" + latest), current, out _).CheckAsync(CancellationToken.None);
        Assert.Equal(newer ? UpdateStatus.Available : UpdateStatus.UpToDate, result!.Status);
    }

    [Fact]
    public async Task FallsBackToFirstZipWhenNoPortableName()
    {
        var assets = """{"name":"notes.txt","browser_download_url":"https://github.com/x/n.txt","size":1},{"name":"a.zip","browser_download_url":"https://github.com/x/a.zip","size":9}""";
        var result = await Checker(ReleaseJson("v2.0.0", assets: assets), "1.0.0", out _).CheckAsync(CancellationToken.None);
        Assert.Equal("https://github.com/x/a.zip", result!.Release.PortableZipUrl!.ToString());
    }

    // ---- DownloadAsync ----

    private static byte[] MakeZip(params string[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            foreach (var name in entries)
                using (var w = new StreamWriter(zip.CreateEntry(name).Open())) w.Write("x");
        return ms.ToArray();
    }

    private static ReleaseInfo Release(string url, long? size) =>
        new("1.2.0", "v1.2.0", new Uri("https://github.com/syedahmad0786/OpenClicky/releases/tag/v1.2.0"), new Uri(url), size, null);

    private UpdateChecker Downloader(byte[] body) =>
        new(new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) })), "1.0.0");

    [Fact]
    public async Task DownloadHappyPathReturnsZipContainingExe()
    {
        var zip = MakeZip("OpenClicky-1.2.0/OpenClicky.exe", "OpenClicky-1.2.0/readme.txt");
        double last = 0;
        var path = await Downloader(zip).DownloadAsync(Release("https://github.com/x/p.zip", zip.Length), _dir,
            new Progress<double>(v => last = v), CancellationToken.None);
        Assert.NotNull(path);
        Assert.StartsWith(_dir, path);
        using var archive = ZipFile.OpenRead(path!);
        Assert.Contains(archive.Entries, e => e.Name == "OpenClicky.exe");
        await Task.Delay(50); // Progress<T> posts asynchronously
        Assert.Equal(1.0, last);
    }

    [Fact]
    public async Task DownloadSizeMismatchReturnsNullAndRemovesFile()
    {
        var zip = MakeZip("OpenClicky.exe");
        var path = await Downloader(zip).DownloadAsync(Release("https://github.com/x/p.zip", zip.Length + 1), _dir, null, CancellationToken.None);
        Assert.Null(path);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task DownloadWithoutExeReturnsNullAndRemovesFile()
    {
        var zip = MakeZip("other.exe");
        var path = await Downloader(zip).DownloadAsync(Release("https://github.com/x/p.zip", zip.Length), _dir, null, CancellationToken.None);
        Assert.Null(path);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task DownloadNotAZipReturnsNull()
    {
        var path = await Downloader([1, 2, 3]).DownloadAsync(Release("https://github.com/x/p.zip", 3), _dir, null, CancellationToken.None);
        Assert.Null(path);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Theory]
    [InlineData("https://evil.example.com/p.zip")]
    [InlineData("http://github.com/p.zip")]
    [InlineData("https://github.com.evil.com/p.zip")]
    [InlineData("https://x.githubusercontent.com.evil.com/p.zip")]
    public async Task DownloadRejectsNonGitHubHosts(string url)
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(MakeZip("OpenClicky.exe")) });
        var path = await new UpdateChecker(new HttpClient(handler), "1.0.0").DownloadAsync(Release(url, null), _dir, null, CancellationToken.None);
        Assert.Null(path);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task DownloadAllowsUnknownSize()
    {
        var zip = MakeZip("OpenClicky.exe");
        var path = await Downloader(zip).DownloadAsync(Release("https://github.com/x/p.zip", null), _dir, null, CancellationToken.None);
        Assert.NotNull(path);
    }

    [Theory]
    [InlineData("https://objects.githubusercontent.com/p.zip")]
    [InlineData("https://release-assets.githubusercontent.com/p.zip")]
    public async Task DownloadAcceptsRedirectToGitHubAssetHosts(string finalUrl)
    {
        var zip = MakeZip("OpenClicky.exe");
        var path = await RedirectingDownloader(zip, finalUrl).DownloadAsync(Release("https://github.com/x/p.zip", zip.Length), _dir, null, CancellationToken.None);
        Assert.NotNull(path);
        Assert.Empty(Directory.GetFiles(_dir, "*.part"));
    }

    [Theory]
    [InlineData("https://evil.example/p.zip")]
    [InlineData("https://objects.githubusercontent.com.evil.com/p.zip")]
    [InlineData("https://other.githubusercontent.com/p.zip")]
    [InlineData("http://objects.githubusercontent.com/p.zip")]
    public async Task DownloadRejectsRedirectToOtherHostsAndLeavesNoFile(string finalUrl)
    {
        var zip = MakeZip("OpenClicky.exe");
        var path = await RedirectingDownloader(zip, finalUrl).DownloadAsync(Release("https://github.com/x/p.zip", zip.Length), _dir, null, CancellationToken.None);
        Assert.Null(path);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task DownloadWithNoFinalRequestUriIsFailure()
    {
        var handler = new NullRequestMessageHandler(MakeZip("OpenClicky.exe"));
        var path = await new UpdateChecker(new HttpClient(handler), "1.0.0").DownloadAsync(Release("https://github.com/x/p.zip", null), _dir, null, CancellationToken.None);
        Assert.Null(path);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task DownloadFromAssetCdnAsInitialUrlIsRejected()
    {
        var zip = MakeZip("OpenClicky.exe");
        Assert.Null(await Downloader(zip).DownloadAsync(Release("https://objects.githubusercontent.com/p.zip", null), _dir, null, CancellationToken.None));
    }

    [Fact]
    public async Task HttpClientTimeoutDuringDownloadReturnsNullWithoutFile()
    {
        var handler = new FakeHandler(_ => throw new TaskCanceledException("timeout"));
        var path = await new UpdateChecker(new HttpClient(handler), "1.0.0").DownloadAsync(Release("https://github.com/x/p.zip", 10), _dir, null, CancellationToken.None);
        Assert.Null(path);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task CallerCancellationDuringDownloadPropagatesWithoutFile()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var handler = new CancelAwareHandler();
        var checker = new UpdateChecker(new HttpClient(handler), "1.0.0");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => checker.DownloadAsync(Release("https://github.com/x/p.zip", 10), _dir, null, cts.Token));
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task FailedRedownloadKeepsPreviousGoodZip()
    {
        var good = MakeZip("OpenClicky.exe");
        var first = await Downloader(good).DownloadAsync(Release("https://github.com/x/p.zip", good.Length), _dir, null, CancellationToken.None);
        Assert.NotNull(first);
        var bad = MakeZip("nope.txt");
        Assert.Null(await Downloader(bad).DownloadAsync(Release("https://github.com/x/p.zip", bad.Length), _dir, null, CancellationToken.None));
        Assert.Equal(good, await File.ReadAllBytesAsync(first!));
        Assert.Single(Directory.GetFiles(_dir));
    }

    private UpdateChecker RedirectingDownloader(byte[] body, string finalUrl) =>
        new(new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(body),
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, finalUrl),
        })), "1.0.0");

    private sealed class NullRequestMessageHandler(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }

    // ---- current-version parsing ----

    [Theory]
    [InlineData("0.6.0.0", "v0.6.1", true)]
    [InlineData("0.6.0.0", "v0.6.0", false)]
    [InlineData("0.6.0.3", "v0.6.0", false)]
    [InlineData("1.0.0.0+abc", "v1.0.1", true)]
    public async Task FourPartCurrentVersionIsAccepted(string current, string tag, bool newer)
    {
        var result = await Checker(ReleaseJson(tag), current, out _).CheckAsync(CancellationToken.None);
        Assert.Equal(newer ? UpdateStatus.Available : UpdateStatus.UpToDate, result!.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("dev")]
    [InlineData("1.x.0")]
    [InlineData("1.2.3.4.5")]
    public async Task UnparseableCurrentVersionFailsClosedWithoutRequest(string current)
    {
        var checker = Checker(ReleaseJson("v9.0.0"), current, out var handler);
        Assert.Null(await checker.CheckAsync(CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task DownloadWithoutZipUrlReturnsNull()
    {
        var release = new ReleaseInfo("1.2.0", "v1.2.0", new Uri("https://github.com/x"), null, null, null);
        Assert.Null(await Downloader([]).DownloadAsync(release, _dir, null, CancellationToken.None));
    }
}
