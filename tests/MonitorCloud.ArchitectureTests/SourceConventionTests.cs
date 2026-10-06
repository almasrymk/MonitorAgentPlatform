using System.Text.RegularExpressions;
using System.Xml.Linq;
using MonitorCloud.TestShared;

namespace MonitorCloud.ArchitectureTests;

/// <summary>Rules 7, 8 and 9 of 09 section 3, checked on the source and package files.</summary>
public sealed partial class SourceConventionTests
{
    /// <summary>The only places allowed to bypass the tenant filters (03 section 4).</summary>
    private static readonly string[] IgnoreQueryFiltersAllowList =
    [
        "Application/Identity/",      // sign-in by e-mail
        "Application/Devices/Token",  // device token exchange
        "Application/Devices/Enroll", // enrollment
        "Infrastructure/Seeding/",    // seeding
        "Infrastructure/Licensing/Sync", // licensing sync
    ];

    [Fact]
    public void IgnoreQueryFilters_is_used_only_in_the_allow_listed_places()
    {
        var offenders = RepositoryPaths.CSharpFiles("src")
            .Where(f => File.ReadAllText(f).Contains(".IgnoreQueryFilters(", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(RepositoryPaths.Source, f).Replace('\\', '/'))
            .Select(f => f.StartsWith("MonitorCloud.", StringComparison.Ordinal) ? f["MonitorCloud.".Length..] : f)
            .Where(f => !IgnoreQueryFiltersAllowList.Any(a => f.StartsWith(a, StringComparison.Ordinal)))
            .ToList();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void Forbidden_packages_are_not_referenced()
    {
        var props = XDocument.Load(Path.Combine(RepositoryPaths.Root, "Directory.Packages.props"));
        var packages = props.Descendants("PackageVersion")
            .ToDictionary(e => e.Attribute("Include")!.Value, e => e.Attribute("Version")!.Value, StringComparer.OrdinalIgnoreCase);

        packages.Keys.ShouldNotContain(k => k.StartsWith("AutoMapper", StringComparison.OrdinalIgnoreCase));
        packages.Keys.ShouldNotContain(k => k.StartsWith("FluentAssertions", StringComparison.OrdinalIgnoreCase));
        packages.Keys.ShouldNotContain(k => k.Contains("Redis", StringComparison.OrdinalIgnoreCase));
        packages.Keys.ShouldNotContain(k => k.Contains("RabbitMQ", StringComparison.OrdinalIgnoreCase));
        packages.Keys.ShouldNotContain(k => k.Contains("Npgsql", StringComparison.OrdinalIgnoreCase));
        packages["MediatR"].ShouldBe("12.5.0");
    }

    [Fact]
    public void Loaded_assemblies_contain_no_forbidden_library()
    {
        var names = Assemblies.Ours.SelectMany(a => a.GetReferencedAssemblies()).Select(a => a.Name!).ToList();

        names.ShouldNotContain(n => n.StartsWith("AutoMapper", StringComparison.Ordinal));
        names.ShouldNotContain(n => n.StartsWith("FluentAssertions", StringComparison.Ordinal));
        var mediatr = Assemblies.Ours.SelectMany(a => a.GetReferencedAssemblies()).FirstOrDefault(a => a.Name == "MediatR");
        if (mediatr is not null)
            mediatr.Version!.Major.ShouldBeLessThan(13);
    }

    [Fact]
    public void Application_does_not_read_the_wall_clock_or_create_random_guids()
    {
        var offenders = RepositoryPaths.CSharpFiles("src/MonitorCloud.Application")
            .SelectMany(f => File.ReadAllLines(f).Select((line, i) => (File: f, Line: i + 1, Text: line)))
            .Where(l => ForbiddenCalls().IsMatch(l.Text))
            .Select(l => $"{Path.GetFileName(l.File)}:{l.Line}")
            .ToList();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void Domain_does_not_read_the_wall_clock()
    {
        var offenders = RepositoryPaths.CSharpFiles("src/MonitorCloud.Domain")
            .Where(f => WallClock().IsMatch(File.ReadAllText(f)))
            .Select(Path.GetFileName)
            .ToList();

        offenders.ShouldBeEmpty();
    }

    [GeneratedRegex(@"\b(DateTime|DateTimeOffset)\.(Now|UtcNow|Today)\b|\bGuid\.NewGuid\(")]
    private static partial Regex ForbiddenCalls();

    [GeneratedRegex(@"\b(DateTime|DateTimeOffset)\.(Now|UtcNow|Today)\b")]
    private static partial Regex WallClock();
}
