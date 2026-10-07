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
                .SendAsync("deviceStateChanged", payload, cancellationToken);
        }

        foreach (var tenantId in changes.Select(c => c.TenantId).Distinct())
            ScheduleSummary(LiveGroups.Tenant(tenantId), "tenant", tenantId);
        foreach (var locationId in changes.Select(c => c.LocationId).Distinct())
            ScheduleSummary(LiveGroups.Location(locationId), "location", locationId);
        if (changes.Count > 0)
            ScheduleSummary(LiveGroups.Platform, "platform", null);
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
