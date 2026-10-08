namespace MonitorCloud.Application.Abstractions.Realtime;

/// <summary>The <c>deviceStateChanged</c> payload of the live hub (06 section 5).</summary>
public sealed record DeviceStateChange(
    Guid DeviceId, Guid TenantId, Guid LocationId, string Connection, string Health, string LicenseState, decimal? Cpu, decimal? Ram, decimal? Disk, DateTimeOffset? LastSeenAt);

/// <summary>The <c>liveSample</c> payload (06 section 5): live mode only, never stored.</summary>
public sealed record LiveSampleChange(Guid DeviceId, DateTimeOffset At, decimal Cpu, decimal Ram, decimal DiskActive, long RxBps, long TxBps, decimal? CpuTempC);

/// <summary>The <c>alertRaised</c> / <c>alertUpdated</c> / <c>alertResolved</c> payload (06 section 5); the portal reloads its lists.</summary>
public sealed record AlertChange(string Kind, Guid AlertId, Guid TenantId, Guid LocationId, Guid DeviceId, string Severity, string? Title);

/// <summary>The <c>notificationCreated</c> payload; <see cref="TenantId"/> null = platform feed.</summary>
public sealed record NotificationChange(Guid NotificationId, Guid? TenantId, string Severity, string Title, Guid? LocationId);

/// <summary>Pushes live events to the portal (SignalR in the API host; a no-op elsewhere).</summary>
public interface ILiveNotifier
{
    Task DeviceStateChangedAsync(IReadOnlyCollection<DeviceStateChange> changes, CancellationToken cancellationToken);

    Task LiveSampleAsync(LiveSampleChange sample, CancellationToken cancellationToken);

    Task SnapshotUpdatedAsync(Guid deviceId, DateTimeOffset capturedAt, CancellationToken cancellationToken);

    Task AlertChangedAsync(AlertChange change, CancellationToken cancellationToken);

    Task NotificationCreatedAsync(NotificationChange change, CancellationToken cancellationToken);
}

internal sealed class NoLiveNotifier : ILiveNotifier
{
    public Task DeviceStateChangedAsync(IReadOnlyCollection<DeviceStateChange> changes, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task LiveSampleAsync(LiveSampleChange sample, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SnapshotUpdatedAsync(Guid deviceId, DateTimeOffset capturedAt, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task AlertChangedAsync(AlertChange change, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task NotificationCreatedAsync(NotificationChange change, CancellationToken cancellationToken) => Task.CompletedTask;
}
