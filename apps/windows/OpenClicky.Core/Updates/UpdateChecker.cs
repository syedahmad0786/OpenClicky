using System.Globalization;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;

namespace OpenClicky.Core.Updates;

/// <summary>
/// Checks GitHub for a newer OpenClicky release and downloads its portable ZIP. The caller owns the
/// HttpClient. Network and parse failures yield null; only caller cancellation is thrown.
/// </summary>
public sealed class UpdateChecker
{
    public const string LatestReleaseUrl = "https://api.github.com/repos/syedahmad0786/OpenClicky/releases/latest";
    public const string ReleasesPageUrl = "https://github.com/syedahmad0786/OpenClicky/releases";
    public const long MaxDownloadBytes = 600L * 1024 * 1024;
    public static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(20);

    private readonly HttpClient _http;
    private readonly string _currentVersion;

    public UpdateChecker(HttpClient http, string currentVersion)
    {
        _http = http;
        _currentVersion = StripV(currentVersion.Trim());
    }

    public async Task<UpdateCheckResult?> CheckAsync(CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(CheckTimeout);
            using var request = NewRequest(LatestReleaseUrl, "application/vnd.github+json");
            using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            var json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            return Parse(json);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; } // our 20 s timeout
        catch (Exception e) when (e is HttpRequestException or JsonException or InvalidOperationException or IOException or FormatException or UriFormatException) { return null; }
    }

    private UpdateCheckResult? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;
        var tag = Str(root, "tag_name");
        if (string.IsNullOrWhiteSpace(tag) || !Uri.TryCreate(Str(root, "html_url"), UriKind.Absolute, out var page)) return null;
        var version = StripV(tag.Trim());
        if (ParseSemVer(version) is not { } latest) return null;

        // A pre-release or draft is never offered: report "up to date" with a stand-in for the running version.
        if (Bool(root, "prerelease") || Bool(root, "draft"))
            return new(UpdateStatus.UpToDate, new ReleaseInfo(_currentVersion, "v" + _currentVersion, new Uri(ReleasesPageUrl), null, null, null));

        Uri? zipUrl = null;
        long? zipSize = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            var zips = assets.EnumerateArray()
                .Select(a => (Name: Str(a, "name"), Url: Str(a, "browser_download_url"), Size: a.TryGetProperty("size", out var s) && s.TryGetInt64(out var n) ? n : (long?)null))
                .Where(a => a.Name is not null && a.Url is not null && a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var pick = zips.FirstOrDefault(a => a.Name!.Contains("portable", StringComparison.OrdinalIgnoreCase));
            if (pick.Name is null && zips.Count > 0) pick = zips[0];
            if (pick.Name is not null && Uri.TryCreate(pick.Url, UriKind.Absolute, out zipUrl)) zipSize = pick.Size;
        }

        var release = new ReleaseInfo(version, tag.Trim(), page, zipUrl, zipSize, Str(root, "body"));
        var current = ParseSemVer(_currentVersion) ?? new SemVer(0, 0, 0, null);
        return new(latest.CompareTo(current) > 0 ? UpdateStatus.Available : UpdateStatus.UpToDate, release);
    }

    /// <summary>
    /// Downloads the release's portable ZIP into <paramref name="targetDirectory"/>. Returns the file path, or
    /// null (leaving no file behind) if the URL is not a GitHub https URL, the download fails, exceeds 600 MB,
    /// does not match the advertised size, or is not a ZIP containing OpenClicky.exe at any depth.
    /// </summary>
    public async Task<string?> DownloadAsync(ReleaseInfo release, string targetDirectory, IProgress<double>? progress, CancellationToken ct)
    {
        if (release.PortableZipUrl is not { } url || !IsAllowedHost(url)) return null;
        string? path = null;
        try
        {
            Directory.CreateDirectory(targetDirectory);
            var safe = string.Concat(release.Version.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            path = Path.Combine(targetDirectory, $"OpenClicky-{safe}-portable.zip");

            using var request = NewRequest(url.ToString(), "application/octet-stream");
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            // Redirects (github.com -> *.githubusercontent.com) must stay on GitHub too.
            if (response.RequestMessage?.RequestUri is { } finalUri && !IsAllowedHost(finalUri)) return null;
            var total = response.Content.Headers.ContentLength ?? release.PortableZipSize;
            if (total > MaxDownloadBytes) return null;

            long written = 0;
            await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    written += read;
                    if (written > MaxDownloadBytes) { file.Close(); Discard(path); return null; }
                    await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    if (total > 0) progress?.Report(Math.Min(1.0, written / (double)total.Value));
                }
            }

            if (release.PortableZipSize is { } expected && expected != written) { Discard(path); return null; }
            if (!ContainsExe(path)) { Discard(path); return null; }
            progress?.Report(1.0);
            return path;
        }
        catch (OperationCanceledException) { Discard(path); throw; }
        catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            Discard(path);
            return null;
        }
    }

    // github.com and its asset CDN (objects.githubusercontent.com, or the release-assets.* host GitHub redirects to today).
    private static bool IsAllowedHost(Uri u) =>
        u.Scheme == Uri.UriSchemeHttps &&
        (u.Host == "github.com" || u.Host == "githubusercontent.com" || u.Host.EndsWith(".githubusercontent.com", StringComparison.Ordinal));

    private static bool ContainsExe(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        return zip.Entries.Any(e => e.Name.Equals("OpenClicky.exe", StringComparison.OrdinalIgnoreCase));
    }

    private static void Discard(string? path)
    {
        try { if (path is not null) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static HttpRequestMessage NewRequest(string url, string accept)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("OpenClicky");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        return request;
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static string StripV(string v) => v.Length > 0 && (v[0] == 'v' || v[0] == 'V') ? v[1..] : v;

    /// <summary>major.minor.patch (missing = 0), optional -pre-release; +build metadata ignored.</summary>
    private static SemVer? ParseSemVer(string v)
    {
        var plus = v.IndexOf('+');
        if (plus >= 0) v = v[..plus];
        string? pre = null;
        var dash = v.IndexOf('-');
        if (dash >= 0) { pre = v[(dash + 1)..]; v = v[..dash]; }
        var parts = v.Split('.');
        if (parts.Length > 3) return null;
        var n = new int[3];
        for (var i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out n[i])) return null;
        return new SemVer(n[0], n[1], n[2], string.IsNullOrEmpty(pre) ? null : pre);
    }

    // ponytail: two pre-releases of the same core compare by ordinal string, not full SemVer precedence.
    private readonly record struct SemVer(int Major, int Minor, int Patch, string? Pre)
    {
        public int CompareTo(SemVer o)
        {
            var c = Major.CompareTo(o.Major);
            if (c != 0) return c;
            c = Minor.CompareTo(o.Minor);
            if (c != 0) return c;
            c = Patch.CompareTo(o.Patch);
            if (c != 0) return c;
            if (Pre is null) return o.Pre is null ? 0 : 1;
            return o.Pre is null ? -1 : string.CompareOrdinal(Pre, o.Pre);
        }
    }
}
