namespace MonitorCloud.TestShared;

/// <summary>Locates the repository root (the folder that contains <c>MonitorCloud.slnx</c>) for source-level checks.</summary>
public static class RepositoryPaths
{
    public static string Root { get; } = FindRoot();

    public static string Source => Path.Combine(Root, "src");

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MonitorCloud.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    /// <summary>C# source files under <paramref name="relative"/>, excluding bin, obj and migrations.</summary>
    public static IEnumerable<string> CSharpFiles(string relative) =>
        Directory.EnumerateFiles(Path.Combine(Root, relative), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
}
