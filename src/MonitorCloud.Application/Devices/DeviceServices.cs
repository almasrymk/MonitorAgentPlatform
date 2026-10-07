using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Application.Devices.Contracts;
using MonitorCloud.Application.Licensing.Contracts;
using MonitorCloud.Application.Tenancy.Contracts;
using MonitorCloud.Domain.Devices;
using MonitorCloud.Domain.Licensing;

namespace MonitorCloud.Application.Devices;

internal sealed class DeviceStatsDirectory(IReadDbContext db) : IDeviceStatsDirectory, ILocationDeviceCounter
{
    public async Task<IReadOnlyDictionary<Guid, DeviceCounts>> ByTenantAsync(IReadOnlyCollection<Guid>? tenantIds, CancellationToken ct)
    {
        var query = DeviceQueries.Active(db);
        if (tenantIds is not null)
            query = query.Where(x => tenantIds.Contains(x.State.TenantId));
        return await Count(query.GroupBy(x => x.State.TenantId), ct);
    }

    public async Task<IReadOnlyDictionary<Guid, DeviceCounts>> ByLocationAsync(IReadOnlyCollection<Guid> locationIds, CancellationToken ct) =>
        await Count(DeviceQueries.Active(db).Where(x => locationIds.Contains(x.State.LocationId)).GroupBy(x => x.State.LocationId), ct);

    public async Task<int> CountAsync(Guid locationId, CancellationToken cancellationToken) =>
        await DeviceQueries.Active(db).CountAsync(x => x.State.LocationId == locationId, cancellationToken);

    private static async Task<IReadOnlyDictionary<Guid, DeviceCounts>> Count(IQueryable<IGrouping<Guid, DeviceQueries.Row>> groups, CancellationToken ct) =>
        await groups.Select(g => new
            {
                Key = g.Key,
                Devices = g.Count(),
                Online = g.Count(x => x.State.Connection == ConnectionState.Online),
                Healthy = g.Count(x => x.State.Health == DeviceHealth.Healthy),
                Warning = g.Count(x => x.State.Health == DeviceHealth.Warning),
                Critical = g.Count(x => x.State.Health == DeviceHealth.Critical),
                Licensed = g.Count(x => x.State.LicenseState == LicenseStateValue.Licensed),
            })
            .ToDictionaryAsync(x => x.Key, x => new DeviceCounts(x.Devices, x.Online, x.Devices - x.Online, x.Healthy, x.Warning, x.Critical, x.Licensed, x.Devices - x.Licensed), ct);
}

/// <summary>Licensed / unlicensed devices and usage by OS for Subscription &amp; Licenses (replaces the M2 placeholder).</summary>
internal sealed class DeviceLicenseStats(IReadDbContext db) : IDeviceLicenseStats
{
    public async Task<(int Licensed, int Unlicensed, IReadOnlyList<OsUsageDto> ByOs)> GetAsync(CancellationToken ct)
    {
        var rows = await DeviceQueries.Active(db)
            .GroupBy(x => new { x.State.OsFamily, x.State.LicenseState })
            .Select(g => new { g.Key.OsFamily, g.Key.LicenseState, Count = g.Count() })
            .ToListAsync(ct);
        var licensed = rows.Where(r => r.LicenseState == LicenseStateValue.Licensed).Sum(r => r.Count);
        var unlicensed = rows.Where(r => r.LicenseState == LicenseStateValue.Unlicensed).Sum(r => r.Count);
        IReadOnlyList<OsUsageDto> byOs = rows.Where(r => r.LicenseState == LicenseStateValue.Licensed)
            .GroupBy(r => r.OsFamily)
            .Select(g => new OsUsageDto(g.Key.ToString(), g.Sum(r => r.Count), licensed == 0 ? 0 : Math.Round(100m * g.Sum(r => r.Count) / licensed, 1)))
            .OrderByDescending(o => o.Devices)
            .ToList();
        return (licensed, unlicensed, byOs);
    }
}

/// <summary>Keeps <c>DeviceStates.LicenseState</c> in line with the Licensing module.</summary>
internal sealed class ApplyDeviceLicenseChange(IAppDbContext db, ILicensingPolicy policy, TimeProvider clock) : IIntegrationEventHandler<DeviceLicenseChangedV1>
{
    public async Task HandleAsync(DeviceLicenseChangedV1 integrationEvent, CancellationToken cancellationToken)
    {
        var state = await db.Set<DeviceState>().SingleOrDefaultAsync(s => s.DeviceId == integrationEvent.DeviceId, cancellationToken);
        state?.SetLicense(integrationEvent.Licensed ? LicenseStateValue.Licensed : LicenseStateValue.Unlicensed, clock.GetUtcNow(), policy.UnlicensedGrace);
    }
}

/// <summary>Moves the health of unlicensed devices to Unknown when their grace period ends (D19).</summary>
public sealed class DeviceGraceService(IAppDbContext db, IUnitOfWork unitOfWork, ILicensingPolicy policy, TimeProvider clock)
{
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var grace = policy.UnlicensedGrace;
        var cutoff = now.Subtract(grace);
        var expired = await db.Set<DeviceState>()
            .Where(s => s.LicenseState == LicenseStateValue.Unlicensed && s.UnlicensedSince != null && s.UnlicensedSince <= cutoff && s.Health != DeviceHealth.Unknown)
            .ToListAsync(ct);
        foreach (var state in expired)
            state.Recalculate(now, grace);
        await unitOfWork.SaveChangesAsync(ct);
        return expired.Count;
    }
}
