namespace DeadVault.Store.Services;

public static class DeadVaultPaths
{
    private static readonly Lazy<string> RootDir = new(ResolveRootDirectory);

    public static string RootDirectory => RootDir.Value;
    public static string DataDirectory => EnsureDirectory(Path.Combine(RootDirectory, "data"));
    public static string LogsDirectory => EnsureDirectory(Path.Combine(DataDirectory, "logs"));
    public static string BuildDirectory => Path.Combine(RootDirectory, "build");

    public static string GetDataPath(params string[] segments)
        => CombineFrom(DataDirectory, segments);

    public static string GetBuildPath(params string[] segments)
        => CombineFrom(BuildDirectory, segments);

    public static string EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }

    private static string CombineFrom(string root, IEnumerable<string> segments)
    {
        var path = root;
        foreach (var segment in segments)
            path = Path.Combine(path, segment);

        return path;
    }

    private static string ResolveRootDirectory()
    {
        var envOverride = Environment.GetEnvironmentVariable("DEADVAULT_HOME");
        if (!string.IsNullOrWhiteSpace(envOverride) && Directory.Exists(envOverride))
            return Path.GetFullPath(envOverride);

        var candidates = new[]
        {
            AppContext.BaseDirectory,
            Environment.CurrentDirectory,
            Directory.GetCurrentDirectory(),
        };

        foreach (var candidate in candidates.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            var resolved = TryFindRoot(candidate!);
            if (resolved != null)
                return resolved;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }

    private static string? TryFindRoot(string startPath)
    {
        var dir = new DirectoryInfo(Path.GetFullPath(startPath));

        for (int depth = 0; depth < 12 && dir != null; depth++, dir = dir.Parent)
        {
            var solutionPath = Path.Combine(dir.FullName, "DeadVault.sln");
            var srcDir = Path.Combine(dir.FullName, "src");
            var dataDir = Path.Combine(dir.FullName, "data");

            if (File.Exists(solutionPath) || (Directory.Exists(srcDir) && Directory.Exists(dataDir)))
                return dir.FullName;
        }

        return null;
    }
}
