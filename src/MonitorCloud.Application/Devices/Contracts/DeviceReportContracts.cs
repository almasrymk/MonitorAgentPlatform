namespace MonitorCloud.Application.Devices.Contracts;

public sealed record DeviceReportRow(
    Guid Id, Guid TenantId, string Name, Guid LocationId, string OsFamily, string? OsName, string Connection, string Health, string LicenseState, decimal? Cpu, decimal? Ram,
    decimal? Disk, DateTimeOffset? LastSeenAt, int OpenCritical, int OpenWarning);

/// <summary>Active devices for reports (Reports module), in the caller's scope, ordered by name.</summary>
public interface IDeviceReportReader
{
    Task<IReadOnlyList<DeviceReportRow>> ListAsync(IReadOnlyCollection<Guid>? locationIds, IReadOnlyCollection<Guid>? deviceIds, CancellationToken ct);
}
