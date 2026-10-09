using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Application.Monitoring.Contracts;
using MonitorCloud.Domain.Monitoring;

namespace MonitorCloud.Application.Monitoring;

internal sealed class AlertReportReader(IReadDbContext db) : IAlertReportReader
{
    public async Task<IReadOnlyList<AlertReportRow>> ListAsync(DateTimeOffset from, DateTimeOffset to, IReadOnlyCollection<Guid>? locationIds, IReadOnlyCollection<Guid>? deviceIds, CancellationToken ct)
    {
        var query = db.Query<Alert>().Where(a => a.FirstSeenAt >= from && a.FirstSeenAt < to);
        if (locationIds is { Count: > 0 })
            query = query.Where(a => locationIds.Contains(a.LocationId));
        if (deviceIds is { Count: > 0 })
            query = query.Where(a => deviceIds.Contains(a.DeviceId));
        var rows = await query.OrderByDescending(a => a.FirstSeenAt).ThenBy(a => a.Id).Take(20_000).ToListAsync(ct);
        return rows.Select(a => new AlertReportRow(a.Id, a.TenantId, a.LocationId, a.DeviceId, a.Severity.ToString(), a.Category, a.Title, a.Status.ToString(), a.FirstSeenAt, a.LastSeenAt,
            a.ResolvedAt, a.Occurrences)).ToList();
    }
}
