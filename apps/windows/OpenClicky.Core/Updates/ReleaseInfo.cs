namespace OpenClicky.Core.Updates;

/// <summary>A published GitHub release. <see cref="Version"/> is the tag without a leading v/V.</summary>
public sealed record ReleaseInfo(
    string Version,
    string TagName,
    Uri ReleasePage,
    Uri? PortableZipUrl,
    long? PortableZipSize,
    string? Notes);

public enum UpdateStatus { Available, UpToDate }

/// <summary>
/// Outcome of a successful check. When the status is UpToDate the release is the newest stable release,
/// or, if the latest one is a pre-release/draft, a stand-in describing the running version (no ZIP).
/// </summary>
public sealed record UpdateCheckResult(UpdateStatus Status, ReleaseInfo Release);
