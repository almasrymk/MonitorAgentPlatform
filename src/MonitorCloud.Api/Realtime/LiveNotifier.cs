using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using MonitorCloud.Application.Abstractions.Realtime;
using MonitorCloud.Application.Devices;

namespace MonitorCloud.Api.Realtime;

/// <summary>
/// SignalR side of <see cref="ILiveNotifier"/>: <c>deviceStateChanged</c> to the tenant, location and device groups,
/// and <c>summaryChanged</c> coalesced to at most one per group every 2 s (01 section 8).
/// </summary>
public sealed class LiveNotifier(IHubContext<LiveHub> hub, TimeProvider clock) : ILiveNotifier
{
    public static readonly TimeSpan SummaryInterval = TimeSpan.FromSeconds(2);

    private readonly ConcurrentDictionary<string, byte> _scheduled = new();

    public async Task DeviceStateChangedAsync(IReadOnlyCollection<DeviceStateChange> changes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);
        foreach (var change in changes)
        {
            var payload = new { change.DeviceId, change.Connection, change.Health, change.LicenseState, change.Cpu, change.Ram, change.Disk, change.LastSeenAt, change.LocationId };
            await hub.Clients.Groups(LiveGroups.Tenant(change.TenantId), LiveGroups.Location(change.LocationId), LiveGroups.Device(change.DeviceId))
                // Not the caller's token: a Goodbye arrives just before the agent closes its stream, and the change must
                // still reach the portal after the request is gone.
                .SendAsync("deviceStateChanged", payload, CancellationToken.None);
        }

        foreach (var tenantId in changes.Select(c => c.TenantId).Distinct())
            ScheduleSummary(LiveGroups.Tenant(tenantId), "tenant", tenantId);
        foreach (var locationId in changes.Select(c => c.LocationId).Distinct())
            ScheduleSummary(LiveGroups.Location(locationId), "location", locationId);
        if (changes.Count > 0)
            ScheduleSummary(LiveGroups.Platform, "platform", null);
    }

    public Task LiveSampleAsync(LiveSampleChange sample, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sample);
        return hub.Clients.Group(LiveGroups.Device(sample.DeviceId)).SendAsync("liveSample",
            new { sample.DeviceId, sample.At, sample.Cpu, sample.Ram, sample.DiskActive, sample.RxBps, sample.TxBps, sample.CpuTempC }, CancellationToken.None);
    }

    public Task SnapshotUpdatedAsync(Guid deviceId, DateTimeOffset capturedAt, CancellationToken cancellationToken) =>
        hub.Clients.Group(LiveGroups.Device(deviceId)).SendAsync("snapshotUpdated", new { deviceId, capturedAt }, CancellationToken.None);

    public Task AlertChangedAsync(AlertChange change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        return hub.Clients.Groups(LiveGroups.Tenant(change.TenantId), LiveGroups.Location(change.LocationId), LiveGroups.Device(change.DeviceId), LiveGroups.Platform)
            .SendAsync(change.Kind, new { change.AlertId, change.DeviceId, change.LocationId, change.Severity, change.Title }, CancellationToken.None);
    }

    public Task NotificationCreatedAsync(NotificationChange change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        var groups = change.TenantId is { } tenantId ? new[] { LiveGroups.Tenant(tenantId), LiveGroups.Platform } : [LiveGroups.Platform];
        return hub.Clients.Groups(groups).SendAsync("notificationCreated", new { change.NotificationId, change.Severity, change.Title, change.LocationId }, CancellationToken.None);
    }

    private void ScheduleSummary(string group, string scope, Guid? id)
    {
        if (!_scheduled.TryAdd(group, 0))
            return;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(SummaryInterval, clock);
                _scheduled.TryRemove(group, out _);
                await hub.Clients.Group(group).SendAsync("summaryChanged", new { scope, id });
            }
            catch (Exception ex) when (ex is ObjectDisposedException or OperationCanceledException or InvalidOperationException)
            {
                _scheduled.TryRemove(group, out _);
            }
        });
    }
}
