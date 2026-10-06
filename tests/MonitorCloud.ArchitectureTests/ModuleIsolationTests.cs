using NetArchTest.Rules;

namespace MonitorCloud.ArchitectureTests;

/// <summary>Rule 6: modules reference each other by id only, and in Application only through <c>&lt;Module&gt;.Contracts</c>.</summary>
public sealed class ModuleIsolationTests
{
    public static TheoryData<string, string> ModulePairs()
    {
        var data = new TheoryData<string, string>();
        foreach (var a in Assemblies.Modules)
        {
            foreach (var b in Assemblies.Modules.Where(b => b != a))
                data.Add(a, b);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ModulePairs))]
    public void Domain_module_does_not_reference_another_domain_module(string module, string other)
    {
        var result = Types.InAssembly(Assemblies.Domain)
            .That().ResideInNamespaceStartingWith($"MonitorCloud.Domain.{module}")
            .ShouldNot().HaveDependencyOn($"MonitorCloud.Domain.{other}")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue($"{module} -> {other}: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Theory]
    [MemberData(nameof(ModulePairs))]
    public void Application_module_uses_another_module_only_through_its_contracts(string module, string other)
    {
        var offenders = Types.InAssembly(Assemblies.Application)
            .That().ResideInNamespaceStartingWith($"MonitorCloud.Application.{module}")
            .GetTypes()
            .Where(t => DependsOnNonContract(t, other))
            .Select(t => t.FullName)
            .ToList();

        offenders.ShouldBeEmpty($"{module} -> {other}");
    }

    private static bool DependsOnNonContract(Type type, string other)
    {
        var prefix = $"MonitorCloud.Application.{other}.";
        var contracts = $"MonitorCloud.Application.{other}.Contracts";
        var domain = $"MonitorCloud.Domain.{other}";
        return Types.InAssembly(Assemblies.Application)
            .That().HaveName(type.Name).And().ResideInNamespace(type.Namespace!)
            .Should().HaveDependencyOnAny(prefix.TrimEnd('.'), domain)
            .GetResult().IsSuccessful
            && !Types.InAssembly(Assemblies.Application)
                .That().HaveName(type.Name).And().ResideInNamespace(type.Namespace!)
                .Should().HaveDependencyOn(contracts)
                .GetResult().IsSuccessful;
    }

    [Fact]
    public void Every_module_has_a_known_name()
    {
        var namespaces = Assemblies.Domain.GetTypes().Concat(Assemblies.Application.GetTypes())
            .Select(t => t.Namespace)
            .OfType<string>()
            .Select(n => n.Split('.'))
            .Where(p => p.Length > 2 && (p[1] == "Domain" || p[1] == "Application"))
            .Select(p => p[2])
            .Distinct(StringComparer.Ordinal);

        var allowed = Assemblies.Modules.Concat(Assemblies.SharedModules).Concat(["Abstractions", "Behaviors", "Common"]);
        namespaces.ShouldAllBe(n => allowed.Contains(n));
    }
}
