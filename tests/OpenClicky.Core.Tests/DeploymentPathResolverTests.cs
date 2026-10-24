using OpenClicky.Core;

namespace OpenClicky.Core.Tests;

public sealed class DeploymentPathResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "OpenClicky.RuntimePath.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void PackagedLayoutUsesExecutableDirectoryInsteadOfSingleFileExtractionDirectory()
    {
        var package = Path.Combine(_root, "package");
        var extraction = Path.Combine(_root, "single-file-extraction");
        var workspace = Path.Combine(package, "runtime", "workspace");
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(extraction);

        var actual = DeploymentPathResolver.ResolveDirectory(
            "workspace",
            Path.Combine(package, "OpenClicky.App.exe"),
            extraction);

        Assert.Equal(Path.GetFullPath(workspace), actual);
    }

    [Fact]
    public void RepositoryLayoutResolvesWorkspaceAndChromeExtension()
    {
        var repo = Path.Combine(_root, "repo");
        var build = Path.Combine(repo, "apps", "windows", "OpenClicky.App", "bin", "Release");
        Directory.CreateDirectory(build);
        File.WriteAllText(Path.Combine(repo, "OpenClicky.sln"), string.Empty);

        Assert.Equal(
            Path.Combine(repo, "apps", "companion-workspace", "dist"),
            DeploymentPathResolver.ResolveDirectory("workspace", null, build));
        Assert.Equal(
            Path.Combine(repo, "apps", "chrome-extension"),
            DeploymentPathResolver.ResolveDirectory("chrome-extension", null, build));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
