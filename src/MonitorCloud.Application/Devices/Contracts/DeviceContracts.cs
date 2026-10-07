namespace MonitorCloud.Application.Devices.Contracts;

public sealed record DeviceCounts(int Devices, int Online, int Offline, int Healthy, int Warning, int Critical, int Licensed, int Unlicensed)
{
    public static DeviceCounts Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>Health score = healthy / total devices (D14), null without devices.</summary>
    public decimal? HealthScore => Devices == 0 ? null : Math.Round(100m * Healthy / Devices, 1);
}

/// <summary>Device counts read from <c>DeviceStates</c> for other modules (Tenancy cards, dashboards).</summary>
public interface IDeviceStatsDirectory
{
    Task<IReadOnlyDictionary<Guid, DeviceCounts>> ByTenantAsync(IReadOnlyCollection<Guid>? tenantIds, CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, DeviceCounts>> ByLocationAsync(IReadOnlyCollection<Guid> locationIds, CancellationToken ct);
}
