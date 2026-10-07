using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Application.Devices.Contracts;
using MonitorCloud.Domain.Devices;

namespace MonitorCloud.Application.Devices;

internal sealed class DeviceDashboardReader(IReadDbContext db) : IDeviceDashboardReader
{
    public async Task<IReadOnlyList<ProblemDevice>> TopProblematicAsync(Guid? locationId, int take, CancellationToken ct)
    {
        var query = Scoped(locationId).Where(x => x.State.Health != DeviceHealth.Healthy);
        var rows = await query
            .OrderBy(x => x.State.Health == DeviceHealth.Critical ? 0 : x.State.Health == DeviceHealth.Warning ? 1 : 2)
            .ThenByDescending(x => x.State.OpenCritical + x.State.OpenWarning)
            .ThenBy(x => x.Device.Name)
            .Take(take)
            .Select(x => new
            {
                x.Device.Id, x.Device.Name, x.State.TenantId, x.State.LocationId, x.State.Health, x.State.Connection, x.State.LicenseState,
                Open = x.State.OpenCritical + x.State.OpenWarning, x.State.LastSeenAt,
            })
            .ToListAsync(ct);
        return rows.Select(r => new ProblemDevice(
            r.Id, r.Name, r.TenantId, r.LocationId, r.Health.ToString(), r.Connection.ToString(),
            r.Connection == ConnectionState.Offline ? "offline" : r.LicenseState == LicenseStateValue.Unlicensed && r.Health == DeviceHealth.Unknown ? "unlicensed" : r.Health == DeviceHealth.Critical ? "critical" : "warning",
            r.Open, DeviceQueries.Utc(r.LastSeenAt))).ToList();
    }

    public async Task<IReadOnlyList<OsCount>> ByOsAsync(Guid? locationId, CancellationToken ct) =>
        (await Scoped(locationId).GroupBy(x => x.State.OsFamily).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct))
        .OrderByDescending(x => x.Count).ThenBy(x => x.Key)
        .Select(x => new OsCount(x.Key.ToString(), x.Count))
        .ToList();

    public async Task<ResourceAverages> ResourceAveragesAsync(Guid? locationId, CancellationToken ct)
    {
        var online = Scoped(locationId).Where(x => x.State.Connection == ConnectionState.Online);
        var averages = await online.GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Cpu = g.Average(x => x.State.CpuPercent), Ram = g.Average(x => x.State.RamPercent), Disk = g.Average(x => x.State.DiskPercent) })
            .SingleOrDefaultAsync(ct);
        return averages is null
            ? new ResourceAverages(0, null, null, null)
            : new ResourceAverages(averages.Count, Round(averages.Cpu), Round(averages.Ram), Round(averages.Disk));
    }

    public async Task<(int Current, int Previous)> EnrolledAsync(DateTimeOffset previousSince, DateTimeOffset since, CancellationToken ct)
    {
        var active = DeviceQueries.Active(db);
        var current = await active.CountAsync(x => x.Device.EnrolledAt >= since, ct);
        var previous = await active.CountAsync(x => x.Device.EnrolledAt >= previousSince && x.Device.EnrolledAt < since, ct);
        return (current, previous);
    }

    public async Task<DateTimeOffset?> LastSeenAsync(Guid locationId, CancellationToken ct) =>
        DeviceQueries.Utc(await Scoped(locationId).MaxAsync(x => x.State.LastSeenAt, ct));

    private IQueryable<DeviceQueries.Row> Scoped(Guid? locationId)
    {
        var query = DeviceQueries.Active(db);
        return locationId is { } id ? query.Where(x => x.State.LocationId == id) : query;
    }

    private static decimal? Round(decimal? value) => value is { } v ? Math.Round(v, 1) : null;
}
