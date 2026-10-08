namespace MonitorCloud.Application.Tenancy.Contracts;

public sealed record TenantInfo(Guid Id, string Name, string Code, string Status, string TimeZone = "Africa/Cairo")
{
    public bool IsActive => Status == "Active";
}

/// <summary>Read access to tenants for other modules (Identity, the tenant-context middleware).</summary>
public interface ITenantDirectory
{
    Task<TenantInfo?> FindAsync(Guid tenantId, CancellationToken cancellationToken);
}

public interface ILocationDirectory
{
    /// <summary>The subset of <paramref name="locationIds"/> that belongs to the tenant.</summary>
    Task<IReadOnlyCollection<Guid>> ExistingAsync(Guid tenantId, IReadOnlyCollection<Guid> locationIds, CancellationToken cancellationToken);
}

/// <summary>Location codes are unique per tenant, also across locations hidden by the caller's location scope.</summary>
public interface ILocationCodeLookup
{
    Task<bool> IsTakenAsync(string code, Guid? exceptLocationId, CancellationToken cancellationToken);
}

/// <summary>Device counts per location, provided by the Devices module (M3).</summary>
public interface ILocationDeviceCounter
{
    Task<int> CountAsync(Guid locationId, CancellationToken cancellationToken);
}
