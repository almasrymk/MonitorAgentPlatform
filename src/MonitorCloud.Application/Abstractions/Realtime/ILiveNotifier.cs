namespace MonitorCloud.Application.Abstractions.Realtime;

/// <summary>The <c>deviceStateChanged</c> payload of the live hub (06 section 5).</summary>
public sealed record DeviceStateChange(
    Guid DeviceId, Guid TenantId, Guid LocationId, string Connection, string Health, string LicenseState, decimal? Cpu, decimal? Ram, decimal? Disk, DateTimeOffset? LastSeenAt);

/// <summary>Pushes live events to the portal (SignalR in the API host; a no-op elsewhere).</summary>
public interface ILiveNotifier
{
    Task DeviceStateChangedAsync(IReadOnlyCollection<DeviceStateChange> changes, CancellationToken cancellationToken);
}

internal sealed class NoLiveNotifier : ILiveNotifier
{
    public Task DeviceStateChangedAsync(IReadOnlyCollection<DeviceStateChange> changes, CancellationToken cancellationToken) => Task.CompletedTask;
}
