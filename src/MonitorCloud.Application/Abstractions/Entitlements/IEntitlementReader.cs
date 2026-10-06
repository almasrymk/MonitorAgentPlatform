namespace MonitorCloud.Application.Abstractions.Entitlements;

public sealed record EntitlementSnapshot(IReadOnlyCollection<string> Features, bool IsExpired)
{
    public bool Has(string feature) => Features.Contains(feature, StringComparer.OrdinalIgnoreCase);
}

/// <summary>Reads the cached entitlement of a tenant (implemented by the Licensing module).</summary>
public interface IEntitlementReader
{
    Task<EntitlementSnapshot> GetAsync(Guid tenantId, CancellationToken cancellationToken);
}
