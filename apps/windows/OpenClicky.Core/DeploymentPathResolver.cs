namespace OpenClicky.Core;

public static class DeploymentPathResolver
{
    public static string ResolveDirectory(
        string name,
        string? processPath = null,
        string? appBaseDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var roots = new List<string>();
        AddProcessDirectory(roots, processPath ?? Environment.ProcessPath);
        AddDirectory(roots, appBaseDirectory ?? AppContext.BaseDirectory);

        foreach (var root in roots)
        {
            var packaged = name == "chrome-extension"
                ? Path.Combine(root, name)
                : Path.Combine(root, "runtime", name);
            if (Directory.Exists(packaged)) return Path.GetFullPath(packaged);
        }

        foreach (var root in roots)
            for (var directory = new DirectoryInfo(root); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "OpenClicky.sln")))
                    return Path.GetFullPath(name switch
                    {
                        "workspace" => Path.Combine(directory.FullName, "apps", "companion-workspace", "dist"),
                        "chrome-extension" => Path.Combine(directory.FullName, "apps", "chrome-extension"),
                        _ => Path.Combine(directory.FullName, "artifacts", "runtime", name)
                    });

        var fallbackRoot = roots.FirstOrDefault() ?? Path.GetFullPath(AppContext.BaseDirectory);
        return Path.GetFullPath(name == "chrome-extension"
            ? Path.Combine(fallbackRoot, name)
            : Path.Combine(fallbackRoot, "runtime", name));
    }

    private static void AddProcessDirectory(List<string> roots, string? processPath)
    {
        if (string.IsNullOrWhiteSpace(processPath)) return;
        AddDirectory(roots, Path.GetDirectoryName(Path.GetFullPath(processPath)));
    }

    private static void AddDirectory(List<string> roots, string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return;
        var fullPath = Path.GetFullPath(directory);
        if (!roots.Contains(fullPath, StringComparer.OrdinalIgnoreCase)) roots.Add(fullPath);
    }
}
