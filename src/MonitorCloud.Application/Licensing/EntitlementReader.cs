using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MonitorCloud.Application.Abstractions.Entitlements;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Application.Licensing.Contracts;
using MonitorCloud.Domain.Licensing;

namespace MonitorCloud.Application.Licensing;

/// <summary>
/// Entitlements for the pipeline's feature checks. A missing entitlement means no features (04 section 1);
/// when Licensing is down the cached features stay in force (fail open, 04 section 4.5).
/// </summary>
internal sealed class EntitlementReader(IReadDbContext db, IMemoryCache cache) : IEntitlementReader, IIntegrationEventHandler<EntitlementChangedV1>
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(10);

    public static string Key(Guid tenantId) => $"entitlement:{tenantId:N}";

    public async Task<EntitlementSnapshot> GetAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(Key(tenantId), out EntitlementSnapshot? cached) && cached is not null)
            return cached;

        var entitlement = await db.Query<TenantEntitlement>().SingleOrDefaultAsync(e => e.TenantId == tenantId, cancellationToken);
        var snapshot = entitlement is null
            ? new EntitlementSnapshot([], IsExpired: false)
            : new EntitlementSnapshot([.. entitlement.Features], entitlement.IsExpired);
        cache.Set(Key(tenantId), snapshot, Ttl);
        return snapshot;
    }

    public Task HandleAsync(EntitlementChangedV1 integrationEvent, CancellationToken cancellationToken)
    {
        cache.Remove(Key(integrationEvent.TenantId));
        return Task.CompletedTask;
    }
}

internal sealed class EntitlementDirectory(IReadDbContext db, IOptions<LicensingSettings> settings, TimeProvider clock) : IEntitlementDirectory
{
    private TimeSpan Window => TimeSpan.FromDays(settings.Value.ExpiringSoonDays);

    public async Task<IReadOnlyDictionary<Guid, EntitlementSummary>> GetAsync(IReadOnlyCollection<Guid> tenantIds, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var rows = await db.Query<TenantEntitlement>().Where(e => tenantIds.Contains(e.TenantId)).ToListAsync(ct);
        return rows.ToDictionary(e => e.TenantId, e => new EntitlementSummary(
            e.TenantId, e.PlanCode, e.PlanName, e.SubscriptionStatus.ToString(), e.MaxDevices, e.ActiveSeats, e.RenewsAt, e.IsExpiringSoon(now, Window)));
    }

    public async Task<IReadOnlyCollection<Guid>> FindTenantsAsync(string? planCode, string? subscriptionStatus, bool expiringSoon, CancellationToken ct)
    {
        var query = db.Query<TenantEntitlement>();
        if (!string.IsNullOrWhiteSpace(planCode))
            query = query.Where(e => e.PlanCode == planCode);
        if (Enum.TryParse<SubscriptionStatus>(subscriptionStatus, true, out var status))
            query = query.Where(e => e.SubscriptionStatus == status);
        if (expiringSoon)
        {
            var now = clock.GetUtcNow();
            var until = now.Add(Window);
            query = query.Where(e => (e.SubscriptionStatus == SubscriptionStatus.Active || e.SubscriptionStatus == SubscriptionStatus.Trial)
                && e.RenewsAt != null && e.RenewsAt >= now && e.RenewsAt <= until);
        }

        return await query.Select(e => e.TenantId).ToListAsync(ct);
    }

    public async Task<int> CountExpiringSoonAsync(CancellationToken ct) => (await FindTenantsAsync(null, null, expiringSoon: true, ct)).Count;
}
