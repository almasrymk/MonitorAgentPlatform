namespace MonitorCloud.SharedKernel;

/// <summary>Marker for any row that belongs to exactly one tenant (customer). Isolation is enforced by query filters and the write guard.</summary>
public interface ITenantOwned
{
    Guid TenantId { get; }
}

/// <summary>Tenant-owned rows that belong to one location. Users restricted to locations only see their own.</summary>
public interface ILocationScoped : ITenantOwned
{
    Guid LocationId { get; }
}
