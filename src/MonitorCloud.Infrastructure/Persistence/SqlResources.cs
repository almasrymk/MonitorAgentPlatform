using System.Collections.Concurrent;

namespace MonitorCloud.Infrastructure.Persistence;

/// <summary>Raw SQL lives in <c>Infrastructure/**/Sql/*.sql</c> embedded resources (03 section 4).</summary>
public static class SqlResources
{
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.Ordinal);

    /// <summary>Loads <c>{Module}/Sql/{Name}.sql</c> by the key <c>"{Module}.{Name}"</c>.</summary>
    public static string Get(string key) => Cache.GetOrAdd(key, Load);

    public static IEnumerable<string> All() =>
        typeof(SqlResources).Assembly.GetManifestResourceNames().Where(n => n.EndsWith(".sql", StringComparison.Ordinal));

    public static string Read(string resourceName)
    {
        using var stream = typeof(SqlResources).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"SQL resource '{resourceName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string Load(string key)
    {
        var parts = key.Split('.', 2);
        var resource = $"MonitorCloud.Infrastructure.{parts[0]}.Sql.{parts[1]}.sql";
        return Read(resource);
    }
}
