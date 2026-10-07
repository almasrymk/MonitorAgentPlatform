namespace MonitorCloud.Application.Devices.Contracts;

/// <summary>Live mode (05 section 4): asks a connected agent for 2-second samples while someone watches the device.</summary>
public interface ILiveModeControl
{
    /// <summary>Records interest for 60 s and (re)sends <c>SetTelemetryMode(LIVE)</c> when needed; false when the device is not connected.</summary>
    Task<bool> RequestLiveAsync(Guid deviceId, CancellationToken cancellationToken);
}

internal sealed class NoLiveModeControl : ILiveModeControl
{
    public Task<bool> RequestLiveAsync(Guid deviceId, CancellationToken cancellationToken) => Task.FromResult(false);
}

public sealed record DeviceRef(Guid Id, Guid TenantId, Guid LocationId, string Name);

/// <summary>Finds an active device in the caller's scope (other modules check device ids with it).</summary>
public interface IDeviceDirectory
{
    Task<DeviceRef?> FindAsync(Guid deviceId, CancellationToken cancellationToken);
}
