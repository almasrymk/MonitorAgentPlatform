using MonitorCloud.Application.Abstractions.Entitlements;

namespace MonitorCloud.Infrastructure.Licensing;

/// <summary>
/// Placeholder until the Licensing module (M2): no tenant has any feature, so feature-gated requests are refused.
/// </summary>
internal sealed class NoEntitlementReader : IEntitlementReader
{
    private static readonly EntitlementSnapshot None = new([], IsExpired: false);

    public Task<EntitlementSnapshot> GetAsync(Guid tenantId, CancellationToken cancellationToken) => Task.FromResult(None);
}
