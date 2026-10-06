using MonitorCloud.Application.Abstractions.Context;

namespace MonitorCloud.Infrastructure.Persistence;

/// <summary>
/// Scoped holder of the tenant filter. Starts closed (no tenant, not unrestricted: sees nothing) until the HTTP
/// middleware or a background job sets it.
/// </summary>
public sealed class TenantScope : ITenantContext, ITenantScopeSetter
{
    private static readonly Guid[] NoLocations = [];

    public Guid? TenantId { get; private set; }
    public IReadOnlyCollection<Guid> LocationScope { get; private set; } = NoLocations;
    public bool IsUnrestricted { get; private set; }

    public void RunAsSystem()
    {
        TenantId = null;
        LocationScope = NoLocations;
        IsUnrestricted = true;
    }

    public void RunAsTenant(Guid tenantId, IReadOnlyCollection<Guid>? locationScope = null)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        TenantId = tenantId;
        LocationScope = locationScope ?? NoLocations;
        IsUnrestricted = false;
    }

    /// <summary>A platform user without a workspace: every tenant.</summary>
    public void SetUnrestricted() => RunAsSystem();
}
