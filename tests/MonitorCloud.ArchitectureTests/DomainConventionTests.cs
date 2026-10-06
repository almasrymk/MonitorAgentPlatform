using System.Reflection;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.ArchitectureTests;

/// <summary>Rules 3 and 4 of 09 section 3.</summary>
public sealed class DomainConventionTests
{
    private static IEnumerable<Type> DomainEntities =>
        Assemblies.Domain.GetTypes().Where(t => t.IsClass && !t.IsAbstract && typeof(Entity).IsAssignableFrom(t));

    [Fact]
    public void Domain_entities_have_no_public_setters()
    {
        var offenders = DomainEntities
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.SetMethod?.IsPublic == true)
                .Select(p => $"{t.Name}.{p.Name}"))
            .ToList();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void Domain_entities_have_a_private_parameterless_constructor()
    {
        var offenders = DomainEntities
            .Where(t => t.GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, Type.EmptyTypes) is not { IsPrivate: true })
            .Select(t => t.Name)
            .ToList();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void Every_type_with_a_tenant_id_implements_ITenantOwned()
    {
        var offenders = Assemblies.Domain.GetTypes().Concat(Assemblies.Infrastructure.GetTypes())
            .Where(t => t.IsClass && !t.IsAbstract && !t.Name.EndsWith("Dto", StringComparison.Ordinal))
            .Where(t => !typeof(IDomainEvent).IsAssignableFrom(t))
            .Where(t => t.GetProperty("TenantId", BindingFlags.Public | BindingFlags.Instance)?.PropertyType == typeof(Guid))
            .Where(t => !typeof(ITenantOwned).IsAssignableFrom(t))
            .Select(t => t.FullName)
            .ToList();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void Every_entity_with_an_optional_tenant_id_implements_IOptionallyTenantOwned()
    {
        // Outbox messages are infrastructure plumbing, read only by the dispatcher (system scope).
        var offenders = DomainEntities.Concat(Assemblies.Infrastructure.GetTypes().Where(t => t.Namespace?.EndsWith(".Messaging", StringComparison.Ordinal) != true))
            .Where(t => t.IsClass && !t.IsAbstract)
            .Where(t => !typeof(MonitorCloud.Application.Abstractions.Context.ITenantContext).IsAssignableFrom(t) && !typeof(IDomainEvent).IsAssignableFrom(t))
            .Where(t => t.GetProperty("TenantId", BindingFlags.Public | BindingFlags.Instance)?.PropertyType == typeof(Guid?))
            .Where(t => !t.Name.EndsWith("Dto", StringComparison.Ordinal) && !t.Name.EndsWith("Info", StringComparison.Ordinal))
            .Where(t => !typeof(IOptionallyTenantOwned).IsAssignableFrom(t))
            .Select(t => t.FullName)
            .ToList();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void Entities_scoped_to_a_location_are_tenant_owned()
    {
        typeof(ITenantOwned).IsAssignableFrom(typeof(ILocationScoped)).ShouldBeTrue();
        typeof(ITenantOwned).IsAssignableFrom(typeof(ILocationAggregate)).ShouldBeTrue();
    }
}
