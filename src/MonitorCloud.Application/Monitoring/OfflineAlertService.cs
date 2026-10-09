using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Application.Devices.Contracts;
using MonitorCloud.Application.Notifications.Contracts;
using MonitorCloud.Domain.Monitoring;

namespace MonitorCloud.Application.Monitoring;

/// <summary>
/// Raises the <c>device-offline</c> alert of devices still offline after the tenant's delay (default 2 minutes,
/// 05 section 2). <c>HostShuttingDown</c> and <c>Updating</c> goodbyes use Info. When every device of a location went
/// offline within 60 s, one location notification replaces the per-device notifications (the alerts stay per device).
/// Runs in the system scope.
/// </summary>
public sealed class OfflineAlertService(
    IAppDbContext db, IUnitOfWork unitOfWork, AlertBook alerts, IDeviceStatsDirectory stats, INotificationPublisher notifications, Tenancy.Contracts.IPlatformDefaults defaults, TimeProvider clock)
{
    public static readonly TimeSpan OutageWindow = TimeSpan.FromSeconds(60);

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var earliest = now.AddMinutes(-1);
        var pending = await db.Set<PendingOfflineAlert>().Where(p => p.WentOfflineAt <= earliest).ToListAsync(ct);
        if (pending.Count == 0)
            return 0;

        var tenantIds = pending.Select(p => p.TenantId).Distinct().ToList();
        var settings = await db.Set<MonitoringSettings>().AsNoTracking().Where(s => tenantIds.Contains(s.TenantId)).ToDictionaryAsync(s => s.TenantId, ct);
        var platformDelay = await defaults.OfflineAlertDelayMinutesAsync(ct);
        MonitoringSettings Setting(Guid tenantId) => settings.GetValueOrDefault(tenantId) ?? MonitoringSettings.Default(tenantId, platformDelay);
        var due = pending
            .Where(p => p.WentOfflineAt.AddMinutes(Setting(p.TenantId).OfflineDelayMinutes) <= now)
            .ToList();
        if (due.Count == 0)
            return 0;

        var online = await stats.ByLocationAsync([.. due.Select(p => p.LocationId).Distinct()], ct);
        foreach (var group in due.GroupBy(p => (p.TenantId, p.LocationId)))
        {
            var items = group.ToList();
            var outage = items.Count >= 2
                && items.Max(p => p.WentOfflineAt) - items.Min(p => p.WentOfflineAt) <= OutageWindow
                && (online.GetValueOrDefault(group.Key.LocationId)?.Online ?? 0) == 0;
            var setting = Setting(group.Key.TenantId);
            foreach (var p in items)
            {
                var planned = p.Reason is "HostShuttingDown" or "Updating";
                var severity = planned ? AlertSeverity.Info : setting.OfflineSeverity;
                var message = p.Reason switch
                {
                    "HostShuttingDown" => "The device is shutting down.",
                    "Updating" => "The agent is being updated.",
                    "ServiceStopping" => "The monitoring service was stopped.",
                    _ => "The device stopped sending heartbeats.",
                };
                await alerts.RaiseAsync(p.TenantId, p.LocationId, p.DeviceId, CloudIssues.DeviceOffline, AlertCategories.Connectivity, severity, "Device is offline", message,
                    AlertSource.Cloud, p.WentOfflineAt, notify: !outage, ct);
                db.Set<PendingOfflineAlert>().Remove(p);
            }

            if (outage)
                await notifications.LocationOutageAsync(group.Key.TenantId, group.Key.LocationId, items.Count, items.Max(p => p.WentOfflineAt), ct);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return due.Count;
    }
}
