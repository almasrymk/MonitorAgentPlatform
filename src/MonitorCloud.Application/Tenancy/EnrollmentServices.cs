using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Application.Tenancy.Contracts;
using MonitorCloud.Domain.Tenancy;

namespace MonitorCloud.Application.Tenancy;

internal sealed class LocationLookup(IReadDbContext db) : ILocationLookup
{
    public async Task<LocationInfo?> DefaultAsync(Guid tenantId, CancellationToken ct) =>
        await db.Query<Location>()
            .Where(l => l.TenantId == tenantId && l.IsDefault)
            .Select(l => new LocationInfo(l.Id, l.TenantId, l.Name, l.Code, l.City, l.Country, l.TimeZone, l.IsDefault))
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, LocationInfo>> GetAsync(IReadOnlyCollection<Guid> locationIds, CancellationToken ct) =>
        locationIds.Count == 0
            ? new Dictionary<Guid, LocationInfo>()
            : await db.Query<Location>()
                .Where(l => locationIds.Contains(l.Id))
                .Select(l => new LocationInfo(l.Id, l.TenantId, l.Name, l.Code, l.City, l.Country, l.TimeZone, l.IsDefault))
                .ToDictionaryAsync(l => l.Id, ct);
}

internal sealed class EnrollmentCodeRedeemer(IAppDbContext db, TimeProvider clock) : IEnrollmentCodeRedeemer
{
    public async Task<Guid?> RedeemAsync(Guid tenantId, string code, CancellationToken ct)
    {
        var hash = EnrollmentCodes.Hash(code);
        var match = await db.Set<LocationEnrollmentCode>().SingleOrDefaultAsync(c => c.TenantId == tenantId && c.CodeHash == hash, ct);
        var now = clock.GetUtcNow();
        if (match is null || !match.CanBeUsed(now))
            return null;
        var location = await db.Set<Location>().SingleOrDefaultAsync(l => l.Id == match.LocationId && l.Status == LocationStatus.Active, ct);
        if (location is null)
            return null;
        match.Use(now);
        return location.Id;
    }
}

internal sealed class TenantNames(IReadDbContext db) : ITenantNames
{
    public async Task<IReadOnlyDictionary<Guid, string>> GetAsync(IReadOnlyCollection<Guid> tenantIds, CancellationToken ct) =>
        tenantIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Query<Tenant>().Where(t => tenantIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, ct);
}
