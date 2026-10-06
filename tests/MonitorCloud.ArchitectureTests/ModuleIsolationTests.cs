using Mono.Cecil;
using Mono.Cecil.Cil;
using MonitorCloud.SharedKernel;
using NetArchTest.Rules;

namespace MonitorCloud.ArchitectureTests;

/// <summary>
/// Rule 6: Domain modules reference each other by id only. In Application, module A uses module B only through
/// <c>Application.B.Contracts</c> or B's integration events (<see cref="IDomainEvent"/> records).
/// </summary>
public sealed class ModuleIsolationTests
{
    private static readonly Lazy<ModuleDefinition> ApplicationModule = new(() => ModuleDefinition.ReadModule(Assemblies.Application.Location));

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
    public void Application_module_uses_another_module_only_through_contracts_or_integration_events(string module, string other)
    {
        var offenders = ApplicationModule.Value.Types
            .Where(t => t.Namespace.StartsWith($"MonitorCloud.Application.{module}", StringComparison.Ordinal))
            .SelectMany(t => ReferencedTypes(t).Select(r => (Type: t.FullName, Reference: r)))
            .Where(x => IsForbidden(x.Reference, other))
            .Select(x => $"{x.Type} -> {x.Reference}")
            .Distinct()
            .ToList();

        offenders.ShouldBeEmpty();
    }

    private static bool IsForbidden(string reference, string other)
    {
        if (reference.StartsWith($"MonitorCloud.Application.{other}.", StringComparison.Ordinal))
            return !reference.StartsWith($"MonitorCloud.Application.{other}.Contracts.", StringComparison.Ordinal);
        if (reference.StartsWith($"MonitorCloud.Domain.{other}.", StringComparison.Ordinal))
        {
            var type = Assemblies.Domain.GetType(reference.Split('<')[0].Replace('/', '+'));
            return type is null || !typeof(IDomainEvent).IsAssignableFrom(type);
        }

        return false;
    }

    /// <summary>Every type referenced by the type's signatures and method bodies, including nested (compiler-generated) types.</summary>
    private static IEnumerable<string> ReferencedTypes(TypeDefinition type)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        void Add(TypeReference? reference)
        {
            if (reference is null)
                return;
            if (reference is GenericInstanceType generic)
            {
                foreach (var argument in generic.GenericArguments)
                    Add(argument);
            }

            var element = reference.GetElementType();
            if (element.DeclaringType is not null)
                Add(element.DeclaringType);
            if (!string.IsNullOrEmpty(element.Namespace))
                names.Add(element.FullName);
        }

        foreach (var t in new[] { type }.Concat(type.NestedTypes))
        {
            Add(t.BaseType);
            foreach (var i in t.Interfaces)
                Add(i.InterfaceType);
            foreach (var f in t.Fields)
                Add(f.FieldType);
            foreach (var p in t.Properties)
                Add(p.PropertyType);
            foreach (var m in t.Methods)
            {
                Add(m.ReturnType);
                foreach (var parameter in m.Parameters)
                    Add(parameter.ParameterType);
                if (!m.HasBody)
                    continue;
                foreach (var variable in m.Body.Variables)
                    Add(variable.VariableType);
                foreach (var instruction in m.Body.Instructions)
                {
                    switch (instruction.Operand)
                    {
                        case TypeReference tr:
                            Add(tr);
                            break;
                        case MethodReference mr:
                            Add(mr.DeclaringType);
                            Add(mr.ReturnType);
                            if (mr is GenericInstanceMethod gm)
                            {
                                foreach (var argument in gm.GenericArguments)
                                    Add(argument);
                            }

                            break;
                        case FieldReference fr:
                            Add(fr.DeclaringType);
                            Add(fr.FieldType);
                            break;
                    }
                }
            }
        }

        return names;
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

    [Fact]
    public void Scanner_detects_a_cross_module_reference() =>
        IsForbidden("MonitorCloud.Application.Tenancy.TenantDirectory", "Tenancy").ShouldBeTrue();

    [Fact]
    public void Scanner_allows_contracts_and_integration_events()
    {
        IsForbidden("MonitorCloud.Application.Tenancy.Contracts.ITenantDirectory", "Tenancy").ShouldBeFalse();
        IsForbidden("MonitorCloud.Domain.Tenancy.TenantSuspendedV1", "Tenancy").ShouldBeFalse();
        IsForbidden("MonitorCloud.Domain.Tenancy.Tenant", "Tenancy").ShouldBeTrue();
    }
}
