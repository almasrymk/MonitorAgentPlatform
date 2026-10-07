namespace MonitorCloud.Application.Licensing.Contracts;

public sealed record EntitlementSummary(
    Guid TenantId, string? PlanCode, string? PlanName, string SubscriptionStatus, int? MaxDevices, int ActiveSeats, DateTimeOffset? RenewsAt, bool ExpiringSoon);

/// <summary>Read access to cached entitlements for other modules (Tenancy cards, dashboards).</summary>
public interface IEntitlementDirectory
{
    Task<IReadOnlyDictionary<Guid, EntitlementSummary>> GetAsync(IReadOnlyCollection<Guid> tenantIds, CancellationToken ct);

    /// <summary>Tenants whose plan code or subscription status matches (null = any).</summary>
    Task<IReadOnlyCollection<Guid>> FindTenantsAsync(string? planCode, string? subscriptionStatus, bool expiringSoon, CancellationToken ct);

    Task<int> CountExpiringSoonAsync(CancellationToken ct);
}

/// <summary>Creates or refreshes a tenant's entitlement from the Licensing Platform (sync, provisioning, linking).</summary>
public interface IEntitlementRefresher
{
    Task<bool> RefreshTenantAsync(Guid tenantId, Guid licensingCustomerId, CancellationToken ct);
}
