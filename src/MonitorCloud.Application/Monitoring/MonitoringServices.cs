using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Application.Monitoring.Contracts;
using MonitorCloud.Domain.Monitoring;

namespace MonitorCloud.Application.Monitoring;

/// <summary>
/// Open alerts that count for the device health. The <c>license</c> alert does not: an unlicensed device keeps its
/// health during the grace period and becomes Unknown after it (00 D19); the licence state is shown on its own.
/// </summary>
internal sealed class OpenAlertCounter(IReadDbContext db) : IOpenAlertCounter
{
    public async Task<OpenAlertCounts> CountAsync(Guid deviceId, CancellationToken ct)
    {
        var rows = await db.Query<Alert>()
            .Where(a => a.DeviceId == deviceId && a.Status == AlertStatus.Open && a.IssueKey != CloudIssues.License)
            .GroupBy(a => a.Severity)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);
        return new OpenAlertCounts(
            rows.Where(r => r.Key == AlertSeverity.Critical).Sum(r => r.Count),
            rows.Where(r => r.Key == AlertSeverity.Warning).Sum(r => r.Count));
    }
}

internal sealed class AlertDashboardReader(IReadDbContext db) : IAlertDashboardReader
{
    public async Task<IReadOnlyDictionary<Guid, string>> WorstOpenTitlesAsync(IReadOnlyCollection<Guid> deviceIds, CancellationToken ct)
    {
        if (deviceIds.Count == 0)
            return new Dictionary<Guid, string>();
        var open = await db.Query<Alert>()
            .Where(a => deviceIds.Contains(a.DeviceId) && a.Status != AlertStatus.Resolved)
            .Select(a => new { a.DeviceId, a.Severity, a.Title, a.LastSeenAt })
            .ToListAsync(ct);
        return open.GroupBy(a => a.DeviceId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.Severity).ThenByDescending(a => a.LastSeenAt).First().Title);
    }

    public async Task<IReadOnlyList<AlertDay>> TrendAsync(Guid? locationId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var query = db.Query<AlertDailyStat>().Where(s => s.Day >= from && s.Day <= to);
        if (locationId is { } id)
            query = query.Where(s => s.LocationId == id);
        var rows = await query
            .GroupBy(s => s.Day)
            .Select(g => new { Day = g.Key, Critical = g.Sum(s => s.Critical), Warning = g.Sum(s => s.Warning), Info = g.Sum(s => s.Info) })
            .ToDictionaryAsync(r => r.Day, ct);
        var days = new List<AlertDay>();
        for (var day = from; day <= to; day = day.AddDays(1))
            days.Add(rows.TryGetValue(day, out var r) ? new AlertDay(day, r.Critical, r.Warning, r.Info) : new AlertDay(day, 0, 0, 0));
        return days;
    }

    public async Task<AlertSeverityCounts> BySeverityAsync(DateTimeOffset since, CancellationToken ct)
    {
        var rows = await db.Query<Alert>()
            .Where(a => a.FirstSeenAt >= since)
            .GroupBy(a => new { a.Severity, a.Status })
            .Select(g => new { g.Key.Severity, g.Key.Status, Count = g.Count() })
            .ToListAsync(ct);
        return new AlertSeverityCounts(
            rows.Where(r => r.Severity == AlertSeverity.Critical).Sum(r => r.Count),
            rows.Where(r => r.Severity == AlertSeverity.Warning).Sum(r => r.Count),
            rows.Where(r => r.Severity == AlertSeverity.Info).Sum(r => r.Count),
            rows.Where(r => r.Status == AlertStatus.Resolved).Sum(r => r.Count));
    }

    public async Task<IReadOnlyList<RecentAlert>> RecentAsync(Guid? locationId, int take, CancellationToken ct)
    {
        var query = db.Query<Alert>().Where(a => a.Status == AlertStatus.Open);
        if (locationId is { } id)
            query = query.Where(a => a.LocationId == id);
        var rows = await query
            .OrderByDescending(a => a.LastSeenAt).ThenBy(a => a.Id)
            .Take(take)
            .Select(a => new { a.Id, a.LastSeenAt, a.Severity, a.TenantId, a.LocationId, a.DeviceId, a.Title, a.Message })
            .ToListAsync(ct);
        return rows.Select(a => new RecentAlert(a.Id, a.LastSeenAt, a.Severity.ToString(), a.TenantId, a.LocationId, a.DeviceId, a.Title, a.Message)).ToList();
    }
}

internal sealed class MonitoringSettingsStore(IAppDbContext db, Tenancy.Contracts.IPlatformDefaults defaults) : IMonitoringSettingsStore
{
    public async Task<OfflineAlertSettings> GetAsync(Guid tenantId, CancellationToken ct)
    {
        var settings = await db.Set<MonitoringSettings>().AsNoTracking().SingleOrDefaultAsync(s => s.TenantId == tenantId, ct)
            ?? MonitoringSettings.Default(tenantId, await defaults.OfflineAlertDelayMinutesAsync(ct));
        return new OfflineAlertSettings(settings.OfflineSeverity.ToString(), settings.OfflineDelayMinutes);
    }

    public async Task SetAsync(Guid tenantId, string severity, int delayMinutes, CancellationToken ct)
    {
        var settings = await db.Set<MonitoringSettings>().SingleOrDefaultAsync(s => s.TenantId == tenantId, ct);
        if (settings is null)
        {
            settings = MonitoringSettings.Default(tenantId);
            db.Set<MonitoringSettings>().Add(settings);
        }

        settings.Update(Enum.Parse<AlertSeverity>(severity, true), delayMinutes);
    }
}

/// <summary>Raises, touches and resolves alerts in the current unit of work (no save).</summary>
public sealed class AlertBook(IAppDbContext db)
{
    public async Task<Alert?> FindOpenAsync(Guid deviceId, string issueKey, CancellationToken ct) =>
        db.Set<Alert>().Local.FirstOrDefault(a => a.DeviceId == deviceId && a.IssueKey == issueKey && a.IsOpen)
        ?? await db.Set<Alert>().SingleOrDefaultAsync(a => a.DeviceId == deviceId && a.IssueKey == issueKey && a.Status == AlertStatus.Open, ct);

    /// <summary>Creates the alert, or touches the open one with the same key. Returns true when a new alert was opened.</summary>
    public async Task<bool> RaiseAsync(
        Guid tenantId, Guid locationId, Guid deviceId, string issueKey, string? category, AlertSeverity severity, string title, string? message, AlertSource source,
        DateTimeOffset at, bool notify, CancellationToken ct)
    {
        var open = await FindOpenAsync(deviceId, issueKey, ct);
        if (open is not null)
        {
            open.Touch(severity, title, message, at);
            return false;
        }

        db.Set<Alert>().Add(Alert.Raise(tenantId, locationId, deviceId, issueKey, category, severity, title, message, source, at, notify));
        return true;
    }

    public async Task<bool> ResolveAsync(Guid deviceId, string issueKey, DateTimeOffset at, CancellationToken ct)
    {
        var open = await FindOpenAsync(deviceId, issueKey, ct);
        if (open is null)
            return false;
        open.Resolve(ResolvedBy.Auto, at);
        return true;
    }
}
