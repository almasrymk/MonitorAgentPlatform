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

/// <summary>The location itself: its <see cref="Entity.Id"/> is the location id used by the location scope.</summary>
public interface ILocationAggregate : ITenantOwned;

/// <summary>
/// Rows that belong to one tenant or to the platform (<c>TenantId</c> null): users, audit records, notifications.
/// Tenant scopes see their own rows only; platform rows are visible to the unrestricted scope only.
/// </summary>
public interface IOptionallyTenantOwned
{
    Guid? TenantId { get; }
}
