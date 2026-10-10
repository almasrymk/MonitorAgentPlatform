namespace MonitorCloud.Api;

/// <summary>
/// MC-1006: in Production the API refuses to start with settings that are only safe on a developer machine. The check
/// names the setting and the reason, never its value (secrets come from environment variables, 01 section 9).
/// </summary>
public static class ProductionReadiness
{
    private static readonly string[] TestMarkers = ["DEV-ONLY", "TEST-ONLY", "CI-ONLY"];
    private static readonly string[] DebugLevels = ["Verbose", "Debug"];

    public static IReadOnlyList<string> Problems(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var problems = new List<string>();
        void Require(bool ok, string problem)
        {
            if (!ok)
                problems.Add(problem);
        }

        Require(!string.IsNullOrWhiteSpace(configuration.GetConnectionString("Monitor")), "ConnectionStrings:Monitor is empty.");

        var userKey = configuration["Jwt:SigningKey"] ?? string.Empty;
        var deviceKey = configuration["Jwt:DeviceSigningKey"] ?? string.Empty;
        foreach (var (name, key) in new[] { ("Jwt:SigningKey", userKey), ("Jwt:DeviceSigningKey", deviceKey) })
        {
            Require(key.Length >= 32, $"{name} must have at least 32 characters.");
            Require(!TestMarkers.Any(m => key.Contains(m, StringComparison.OrdinalIgnoreCase)), $"{name} is a development or test value.");
        }

        Require(userKey.Length == 0 || !string.Equals(userKey, deviceKey, StringComparison.Ordinal), "Jwt:SigningKey and Jwt:DeviceSigningKey must differ.");
        var commandKey = configuration["Commands:SigningKey"] ?? string.Empty;
        Require(commandKey.Contains("PRIVATE KEY", StringComparison.Ordinal), "Commands:SigningKey must hold the ES256 private key (PKCS#8 PEM).");
        Require(!configuration.GetValue("Seed:DemoData", false), "Seed:DemoData must be false (the demo seed refuses Production anyway).");
        Require(string.Equals(configuration["Licensing:Mode"], "Live", StringComparison.OrdinalIgnoreCase), "Licensing:Mode must be Live.");
        Require(!TestMarkers.Any(m => (configuration["Licensing:ClientSecret"] ?? string.Empty).Contains(m, StringComparison.OrdinalIgnoreCase)),
            "Licensing:ClientSecret is a development or test value.");

        var origins = configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
        Require(origins.Length > 0, "Cors:Origins is empty.");
        foreach (var origin in origins)
            Require(IsPublicHttps(origin), $"Cors:Origins contains a non-https or local origin ({Shown(origin)}).");

        foreach (var name in new[] { "Agent:PublicBaseUrl", "Agent:GatewayUrl", "Portal:BaseUrl" })
            Require(IsPublicHttps(configuration[name]), $"{name} must be a public https address.");

        Require(!string.IsNullOrWhiteSpace(configuration["Storage:Root"]), "Storage:Root is empty (files and Data Protection keys need a persistent folder).");
        Require(!string.IsNullOrWhiteSpace(configuration["AllowedHosts"]) && configuration["AllowedHosts"] != "*", "AllowedHosts must list the public host names.");
        Require(!DebugLevels.Contains(configuration["Serilog:MinimumLevel:Default"], StringComparer.OrdinalIgnoreCase), "Serilog:MinimumLevel:Default must be Information or higher.");
        return problems;
    }

    /// <summary>Throws with every problem when <paramref name="environment"/> is Production.</summary>
    public static void EnsureReady(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (!environment.IsProduction())
            return;
        var problems = Problems(configuration);
        if (problems.Count > 0)
            throw new InvalidOperationException("The production configuration is not ready:" + Environment.NewLine + string.Join(Environment.NewLine, problems.Select(p => " - " + p)));
    }

    private static bool IsPublicHttps(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && !uri.IsLoopback
        && !uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);

    /// <summary>Origins are not secret; shown so the operator finds the entry.</summary>
    private static string Shown(string value) => value.Length <= 80 ? value : value[..80];
}
