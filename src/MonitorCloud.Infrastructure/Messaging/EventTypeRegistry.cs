using System.Collections.Frozen;
using System.Reflection;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Infrastructure.Messaging;

/// <summary>Maps outbox type names to event types from the Domain and Application assemblies.</summary>
public sealed class EventTypeRegistry
{
    private readonly FrozenDictionary<string, Type> _types;

    public EventTypeRegistry(IEnumerable<Assembly> assemblies)
    {
        _types = assemblies
            .SelectMany(SafeTypes)
            .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(IDomainEvent).IsAssignableFrom(t))
            .ToFrozenDictionary(t => t.FullName!, StringComparer.Ordinal);
    }

    public Type? Find(string typeName) => _types.GetValueOrDefault(typeName);

    private static IEnumerable<Type> SafeTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>();
        }
    }
}
