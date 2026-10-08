using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Application.Abstractions.Realtime;
using MonitorCloud.Application.Devices.Contracts;
using MonitorCloud.Application.Licensing.Contracts;
using MonitorCloud.Application.Tenancy.Contracts;
using MonitorCloud.Domain.Devices;
using MonitorCloud.Domain.Licensing;
using MonitorCloud.Domain.Monitoring;

namespace MonitorCloud.Application.Monitoring.EventHandlers;

/// <summary>
/// Presence (05 section 2): an offline device gets a pending <c>device-offline</c> alert that the offline job raises
/// after the tenant's delay; coming back online cancels it or resolves the alert. Retired devices close their alerts.
/// </summary>
internal sealed class TrackDevicePresence(IAppDbContext db, AlertBook alerts)
    : IIntegrationEventHandler<DeviceWentOfflineV1>, IIntegrationEventHandler<DeviceCameOnlineV1>, IIntegrationEventHandler<DeviceRetiredV1>, IIntegrationEventHandler<DeviceMovedV1>
{
    public async Task HandleAsync(DeviceWentOfflineV1 integrationEvent, CancellationToken cancellationToken)
    {
        var pending = await db.Set<PendingOfflineAlert>().SingleOrDefaultAsync(p => p.DeviceId == integrationEvent.DeviceId, cancellationToken);
        if (pending is null)
            db.Set<PendingOfflineAlert>().Add(PendingOfflineAlert.Create(integrationEvent.DeviceId, integrationEvent.TenantId, integrationEvent.LocationId, integrationEvent.Reason, integrationEvent.At));
    }

    public async Task HandleAsync(DeviceCameOnlineV1 integrationEvent, CancellationToken cancellationToken)
    {
        await RemovePendingAsync(integrationEvent.DeviceId, cancellationToken);
        await alerts.ResolveAsync(integrationEvent.DeviceId, CloudIssues.DeviceOffline, integrationEvent.At, cancellationToken);
    }

    public async Task HandleAsync(DeviceRetiredV1 integrationEvent, CancellationToken cancellationToken)
    {
        await RemovePendingAsync(integrationEvent.DeviceId, cancellationToken);
        var open = await db.Set<Alert>().Where(a => a.DeviceId == integrationEvent.DeviceId && a.Status == AlertStatus.Open).ToListAsync(cancellationToken);
        foreach (var alert in open)
            alert.Resolve(ResolvedBy.Auto, integrationEvent.At);
    }

    public async Task HandleAsync(DeviceMovedV1 integrationEvent, CancellationToken cancellationToken)
    {
        var open = await db.Set<Alert>().Where(a => a.DeviceId == integrationEvent.DeviceId && a.Status == AlertStatus.Open).ToListAsync(cancellationToken);
        foreach (var alert in open)
            alert.MoveTo(integrationEvent.ToLocationId);
    }

    private async Task RemovePendingAsync(Guid deviceId, CancellationToken ct)
    {
        var pending = await db.Set<PendingOfflineAlert>().SingleOrDefaultAsync(p => p.DeviceId == deviceId, ct);
        if (pending is not null)
            db.Set<PendingOfflineAlert>().Remove(pending);
    }
}

/// <summary>The cloud <c>license</c> alert follows the device's seat (the seat is read again, not taken from the event).</summary>
internal sealed class TrackDeviceLicense(IDeviceDirectory devices, IDeviceSeats seats, AlertBook alerts) : IIntegrationEventHandler<DeviceLicenseChangedV1>
{
    public async Task HandleAsync(DeviceLicenseChangedV1 integrationEvent, CancellationToken cancellationToken)
    {
        var device = await devices.FindAsync(integrationEvent.DeviceId, cancellationToken);
        var seat = await seats.FindAsync(integrationEvent.DeviceId, cancellationToken);
        if (device is null || seat is null)
            return;
        if (seat.State == "Licensed")
        {
            await alerts.ResolveAsync(device.Id, CloudIssues.License, integrationEvent.At, cancellationToken);
            return;
        }

        await alerts.RaiseAsync(device.TenantId, device.LocationId, device.Id, CloudIssues.License, AlertCategories.License, AlertSeverity.Warning, "Device is not licensed",
            seat.ReasonCode is { Length: > 0 } code ? $"The licence was not renewed ({code})." : "The device has no licence seat.", AlertSource.Cloud, integrationEvent.At, true,
            cancellationToken);
    }
}

/// <summary>Three session replacements within 5 minutes (05 section 2, rule 3): a warning on the device.</summary>
internal sealed class TrackCloneSuspicion(IDeviceDirectory devices, AlertBook alerts) : IIntegrationEventHandler<DeviceCloneSuspectedV1>
{
    public async Task HandleAsync(DeviceCloneSuspectedV1 integrationEvent, CancellationToken cancellationToken)
    {
        var device = await devices.FindAsync(integrationEvent.DeviceId, cancellationToken);
        if (device is null)
            return;
        await alerts.RaiseAsync(device.TenantId, device.LocationId, device.Id, CloudIssues.CloneSuspected, AlertCategories.System, AlertSeverity.Warning, "Possible cloned device",
            $"{integrationEvent.Replacements} connections replaced each other within 5 minutes. Another machine may use the same identity.", AlertSource.Cloud,
            integrationEvent.At, true, cancellationToken);
    }
}

/// <summary>Alerts opened per tenant-local day (02 section 6).</summary>
internal sealed class CountAlertsPerDay(IAppDbContext db, ITenantDirectory tenants) : IIntegrationEventHandler<AlertRaisedV1>
{
    public async Task HandleAsync(AlertRaisedV1 integrationEvent, CancellationToken cancellationToken)
    {
        var tenant = await tenants.FindAsync(integrationEvent.TenantId, cancellationToken);
        var day = LocalDay(integrationEvent.At, tenant?.TimeZone);
        var stat = await db.Set<AlertDailyStat>()
            .SingleOrDefaultAsync(s => s.TenantId == integrationEvent.TenantId && s.LocationId == integrationEvent.LocationId && s.Day == day, cancellationToken);
        if (stat is null)
        {
            stat = AlertDailyStat.Create(integrationEvent.TenantId, integrationEvent.LocationId, day);
            db.Set<AlertDailyStat>().Add(stat);
        }

        stat.Count(Enum.Parse<AlertSeverity>(integrationEvent.Severity));
    }

    public static DateOnly LocalDay(DateTimeOffset at, string? timeZone)
    {
        var zone = timeZone is not null && TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var z) ? z : TimeZoneInfo.Utc;
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, zone).DateTime);
    }
}

/// <summary><c>alertRaised</c> / <c>alertUpdated</c> / <c>alertResolved</c> to the tenant, location and device groups (06 section 5).</summary>
internal sealed class PushAlertChanges(ILiveNotifier live)
    : IIntegrationEventHandler<AlertRaisedV1>, IIntegrationEventHandler<AlertSeverityChangedV1>, IIntegrationEventHandler<AlertResolvedV1>
{
    public Task HandleAsync(AlertRaisedV1 integrationEvent, CancellationToken cancellationToken) =>
        live.AlertChangedAsync(new AlertChange("alertRaised", integrationEvent.AlertId, integrationEvent.TenantId, integrationEvent.LocationId, integrationEvent.DeviceId,
            integrationEvent.Severity, integrationEvent.Title), cancellationToken);

    public Task HandleAsync(AlertSeverityChangedV1 integrationEvent, CancellationToken cancellationToken) =>
        live.AlertChangedAsync(new AlertChange("alertUpdated", integrationEvent.AlertId, integrationEvent.TenantId, integrationEvent.LocationId, integrationEvent.DeviceId,
            integrationEvent.To, null), cancellationToken);

    public Task HandleAsync(AlertResolvedV1 integrationEvent, CancellationToken cancellationToken) =>
        live.AlertChangedAsync(new AlertChange("alertResolved", integrationEvent.AlertId, integrationEvent.TenantId, integrationEvent.LocationId, integrationEvent.DeviceId,
            integrationEvent.Severity, null), cancellationToken);
}
