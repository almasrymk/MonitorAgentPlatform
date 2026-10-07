namespace MonitorCloud.Application.Licensing.Contracts;

public sealed record DeviceSeat(Guid DeviceId, Guid LicenseId, string LicenseNumber, string State, string? ReasonCode, string? Token, DateTimeOffset CheckAfter);

/// <summary>Licence rows of devices, written by the Devices module in its own transaction (enrollment).</summary>
public interface IDeviceSeats
{
    /// <summary>Adds or renews the device's seat in the current unit of work (no save).</summary>
    Task RecordLicensedAsync(Guid deviceId, Guid tenantId, string fingerprint, SeatActivation seat, DateTimeOffset now, CancellationToken ct);

    Task<DeviceSeat?> FindAsync(Guid deviceId, CancellationToken ct);
}

/// <summary>The enrollment attempt log (02 section 3).</summary>
public interface IEnrollmentAttemptLog
{
    /// <summary>Adds a successful attempt to the current unit of work.</summary>
    void AddSuccess(Guid tenantId, string productKey, string fingerprint, string hostname, string? ip, DateTimeOffset now);

    /// <summary>Writes a failed attempt immediately (its business transaction does not commit).</summary>
    Task WriteFailureAsync(Guid? tenantId, string? productKey, string? fingerprint, string? hostname, string? ip, string errorCode, DateTimeOffset now, CancellationToken ct);
}

/// <summary>Licensing rules other modules apply (D19).</summary>
public interface ILicensingPolicy
{
    /// <summary>How long an unlicensed device keeps its health before it becomes Unknown.</summary>
    TimeSpan UnlicensedGrace { get; }
}

public sealed record OsUsageDto(string OsFamily, int Devices, decimal Percent);

/// <summary>Devices by licence state and operating system, provided by the Devices module.</summary>
public interface IDeviceLicenseStats
{
    Task<(int Licensed, int Unlicensed, IReadOnlyList<OsUsageDto> ByOs)> GetAsync(CancellationToken ct);
}

/// <summary>Platform-wide plan figures for the Platform Admin Dashboard.</summary>
public sealed record PlanDistribution(string PlanCode, string PlanName, int Customers);

public sealed record ExpiringSubscription(Guid TenantId, string? PlanName, DateTimeOffset RenewsAt, int DaysLeft);

public interface IEntitlementStatistics
{
    Task<IReadOnlyList<PlanDistribution>> DistributionAsync(CancellationToken ct);

    Task<IReadOnlyList<ExpiringSubscription>> ExpiringAsync(int take, CancellationToken ct);
}
