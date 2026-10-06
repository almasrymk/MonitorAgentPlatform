using MonitorCloud.Application.Abstractions.Context;

namespace MonitorCloud.TestShared.Builders;

/// <summary>An explicit, in-memory <see cref="ICurrentUser"/> for handler and behaviour tests.</summary>
public sealed record TestCurrentUser : ICurrentUser
{
    public bool IsAuthenticated { get; init; } = true;
    public ActorType ActorType { get; init; } = ActorType.User;
    public Guid? UserId { get; init; } = Guid.CreateVersion7();
    public Guid? DeviceId { get; init; }
    public Guid? TenantId { get; init; }
    public string? Role { get; init; }
    public string? Name { get; init; } = "Test User";
    public bool IsPlatform { get; init; }
    public IReadOnlyCollection<Guid> LocationScope { get; init; } = [];
    public IReadOnlyCollection<string> Permissions { get; init; } = [];
    public string? IpAddress { get; init; } = "192.0.2.10";
    public string? CorrelationId { get; init; } = "test-correlation";

    public bool HasPermission(string permission) => Permissions.Contains(permission, StringComparer.Ordinal);

    public static TestCurrentUser Anonymous() => new() { IsAuthenticated = false, UserId = null, Name = null };

    public static TestCurrentUser System() => new() { IsAuthenticated = false, ActorType = ActorType.System, UserId = null };

    public static TestCurrentUser Platform(params string[] permissions) =>
        new() { IsPlatform = true, Role = "PlatformAdmin", Permissions = permissions };

    public static TestCurrentUser Tenant(Guid tenantId, params string[] permissions) =>
        new() { TenantId = tenantId, Role = "Administrator", Permissions = permissions };

    public static TestCurrentUser Device(Guid tenantId) =>
        new() { ActorType = ActorType.Device, UserId = null, DeviceId = Guid.CreateVersion7(), TenantId = tenantId };
}

/// <summary>An explicit <see cref="ITenantContext"/>.</summary>
public sealed record TestTenantContext(Guid? TenantId, bool IsUnrestricted, IReadOnlyCollection<Guid> LocationScope) : ITenantContext
{
    public static TestTenantContext Platform() => new(null, true, []);

    public static TestTenantContext Closed() => new(null, false, []);

    public static TestTenantContext ForTenant(Guid tenantId, params Guid[] locations) => new(tenantId, false, locations);
}
