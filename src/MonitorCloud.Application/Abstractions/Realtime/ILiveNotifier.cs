namespace MonitorCloud.Application.Abstractions.Realtime;

/// <summary>The <c>deviceStateChanged</c> payload of the live hub (06 section 5).</summary>
public sealed record DeviceStateChange(
    Guid DeviceId, Guid TenantId, Guid LocationId, string Connection, string Health, string LicenseState, decimal? Cpu, decimal? Ram, decimal? Disk, DateTimeOffset? LastSeenAt);

/// <summary>The <c>liveSample</c> payload (06 section 5): live mode only, never stored.</summary>
public sealed record LiveSampleChange(Guid DeviceId, DateTimeOffset At, decimal Cpu, decimal Ram, decimal DiskActive, long RxBps, long TxBps, decimal? CpuTempC);

/// <summary>Pushes live events to the portal (SignalR in the API host; a no-op elsewhere).</summary>
public interface ILiveNotifier
{
    Task DeviceStateChangedAsync(IReadOnlyCollection<DeviceStateChange> changes, CancellationToken cancellationToken);

    Task LiveSampleAsync(LiveSampleChange sample, CancellationToken cancellationToken);

    Task SnapshotUpdatedAsync(Guid deviceId, DateTimeOffset capturedAt, CancellationToken cancellationToken);
}

internal sealed class NoLiveNotifier : ILiveNotifier
{
    public Task DeviceStateChangedAsync(IReadOnlyCollection<DeviceStateChange> changes, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task LiveSampleAsync(LiveSampleChange sample, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SnapshotUpdatedAsync(Guid deviceId, DateTimeOffset capturedAt, CancellationToken cancellationToken) => Task.CompletedTask;
}
