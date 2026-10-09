using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Application.Devices.Contracts;

namespace MonitorCloud.Application.Devices;

internal sealed class DeviceReportReader(IReadDbContext db) : IDeviceReportReader
{
    public async Task<IReadOnlyList<DeviceReportRow>> ListAsync(IReadOnlyCollection<Guid>? locationIds, IReadOnlyCollection<Guid>? deviceIds, CancellationToken ct)
    {
        var query = DeviceQueries.Active(db);
        if (locationIds is { Count: > 0 })
            query = query.Where(x => locationIds.Contains(x.State.LocationId));
        if (deviceIds is { Count: > 0 })
            query = query.Where(x => deviceIds.Contains(x.Device.Id));
        var rows = await query.OrderBy(x => x.Device.Name).ThenBy(x => x.Device.Id)
            .Select(x => new
            {
                x.Device.Id, x.Device.TenantId, x.Device.Name, x.State.LocationId, x.State.OsFamily, x.Device.OsName, x.State.Connection, x.State.Health, x.State.LicenseState,
                x.State.CpuPercent, x.State.RamPercent, x.State.DiskPercent, x.State.LastSeenAt, x.State.OpenCritical, x.State.OpenWarning,
            })
            .ToListAsync(ct);
        return rows.Select(r => new DeviceReportRow(
            r.Id, r.TenantId, r.Name, r.LocationId, r.OsFamily.ToString(), r.OsName, r.Connection.ToString(), r.Health.ToString(), r.LicenseState.ToString(), r.CpuPercent,
            r.RamPercent, r.DiskPercent, DeviceQueries.Utc(r.LastSeenAt), r.OpenCritical, r.OpenWarning)).ToList();
    }
}
