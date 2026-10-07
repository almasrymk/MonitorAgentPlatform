namespace MonitorCloud.Application.Devices.Contracts;

/// <summary>A device that needs attention: Critical, then Warning, then offline (07 section 5.3).</summary>
public sealed record ProblemDevice(Guid Id, string Name, Guid TenantId, Guid LocationId, string Health, string Connection, string Issue, int OpenAlerts, DateTimeOffset? LastSeenAt);

public sealed record OsCount(string OsFamily, int Devices);

public sealed record ResourceAverages(int OnlineDevices, decimal? Cpu, decimal? Ram, decimal? Disk);

/// <summary>Device figures for the dashboards, read from <c>DeviceStates</c> in the caller's scope.</summary>
public interface IDeviceDashboardReader
{
    Task<IReadOnlyList<ProblemDevice>> TopProblematicAsync(Guid? locationId, int take, CancellationToken ct);

    Task<IReadOnlyList<OsCount>> ByOsAsync(Guid? locationId, CancellationToken ct);

    Task<ResourceAverages> ResourceAveragesAsync(Guid? locationId, CancellationToken ct);

    /// <summary>Devices enrolled in [since, now) and in [previousSince, since).</summary>
    Task<(int Current, int Previous)> EnrolledAsync(DateTimeOffset previousSince, DateTimeOffset since, CancellationToken ct);

    /// <summary>The most recent contact of any device of the location (Location Summary "Last Sync").</summary>
    Task<DateTimeOffset?> LastSeenAsync(Guid locationId, CancellationToken ct);
}
