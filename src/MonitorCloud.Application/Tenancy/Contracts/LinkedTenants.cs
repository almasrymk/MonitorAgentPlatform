namespace MonitorCloud.Application.Tenancy.Contracts;

/// <summary>Tenants linked to Licensing customers, for the entitlement sync.</summary>
public interface ILinkedTenantDirectory
{
    /// <summary>(LicensingCustomerId, TenantId) of every non-archived linked tenant.</summary>
    Task<IReadOnlyList<(Guid CustomerId, Guid TenantId)>> AllLinkedAsync(CancellationToken ct);

    Task<IReadOnlyList<(Guid CustomerId, Guid TenantId)>> FindByLicensingCustomersAsync(IReadOnlyCollection<Guid> customerIds, CancellationToken ct);
}

/// <summary>Auto-provisioning of a tenant for a new Licensing customer (04 section 4.4).</summary>
public interface ITenantProvisioning
{
    /// <summary>Returns the tenant linked to the customer, creating it (and its default location) when missing.</summary>
    Task<ProvisionedTenant> EnsureTenantAsync(Guid licensingCustomerId, string name, string? country, CancellationToken ct);
}

public sealed record ProvisionedTenant(Guid TenantId, bool Created, string Status);
