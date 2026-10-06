namespace MonitorCloud.Application.Abstractions.Context;

public enum ActorType
{
    User,
    Device,
    System,
}

/// <summary>The authenticated caller (03 section 3).</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    ActorType ActorType { get; }
    Guid? UserId { get; }
    Guid? DeviceId { get; }

    /// <summary>The caller's own tenant; null for platform users.</summary>
    Guid? TenantId { get; }
    string? Role { get; }
    string? Name { get; }
    bool IsPlatform { get; }

    /// <summary>Empty = unrestricted inside the tenant.</summary>
    IReadOnlyCollection<Guid> LocationScope { get; }
    bool HasPermission(string permission);
    string? IpAddress { get; }
    string? CorrelationId { get; }
}

/// <summary>The tenant filter in effect for this request or job.</summary>
public interface ITenantContext
{
    /// <summary>Null = all tenants (only when <see cref="IsUnrestricted"/>).</summary>
    Guid? TenantId { get; }
    IReadOnlyCollection<Guid> LocationScope { get; }

    /// <summary>True only for platform users without <c>X-Tenant-Id</c> and for system jobs.</summary>
    bool IsUnrestricted { get; }
}

/// <summary>Sets the scope: the HTTP middleware for requests, background jobs explicitly.</summary>
public interface ITenantScopeSetter
{
    void RunAsSystem();
    void RunAsTenant(Guid tenantId, IReadOnlyCollection<Guid>? locationScope = null);
}
