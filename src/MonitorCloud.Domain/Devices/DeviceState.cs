using MonitorCloud.SharedKernel;

namespace MonitorCloud.Domain.Devices;

public enum ConnectionState : byte
{
    Offline = 0,
    Online = 1,
}

public enum DeviceHealth : byte
{
    Unknown = 0,
    Healthy = 1,
    Warning = 2,
    Critical = 3,
}

public enum LicenseStateValue : byte
{
    Unlicensed = 0,
    Licensed = 1,
}

/// <summary>
/// One narrow row per device that every list and dashboard reads (02 section 4). Not an aggregate: it is written by
/// event handlers, the telemetry writer and the presence monitor.
/// </summary>
public sealed class DeviceState : Entity, ILocationScoped
{
    private DeviceState()
    {
    }

    public Guid DeviceId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid LocationId { get; private set; }
    public ConnectionState Connection { get; private set; }
    public DeviceHealth Health { get; private set; }
    public LicenseStateValue LicenseState { get; private set; }

    /// <summary>When the device lost its licence; drives the D19 grace period.</summary>
    public DateTimeOffset? UnlicensedSince { get; private set; }

    public OsFamily OsFamily { get; private set; }
    public decimal? CpuPercent { get; private set; }
    public decimal? RamPercent { get; private set; }
    public decimal? DiskPercent { get; private set; }
    public long? UptimeSeconds { get; private set; }
    public int OpenCritical { get; private set; }
    public int OpenWarning { get; private set; }
    public DateTime? LastSeenAt { get; private set; }
    public DateTime? LastTelemetryAt { get; private set; }
    public DateTime? ConnectedSince { get; private set; }
    public int? AppliedConfigVersion { get; private set; }
    public long LastEventSequence { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public static DeviceState Create(Guid deviceId, Guid tenantId, Guid locationId, OsFamily osFamily, LicenseStateValue license, DateTimeOffset now) =>
        new()
        {
            Id = deviceId,
            DeviceId = Guard.NotEmpty(deviceId, nameof(DeviceId)),
            TenantId = Guard.NotEmpty(tenantId, nameof(TenantId)),
            LocationId = Guard.NotEmpty(locationId, nameof(LocationId)),
            OsFamily = osFamily,
            LicenseState = license,
            UnlicensedSince = license == LicenseStateValue.Unlicensed ? now : null,
            Connection = ConnectionState.Offline,
            Health = DeviceHealth.Unknown,
            UpdatedAt = now.UtcDateTime,
        };

    public void MoveTo(Guid locationId, DateTimeOffset now)
    {
        LocationId = locationId;
        UpdatedAt = now.UtcDateTime;
    }

    public void SetOsFamily(OsFamily osFamily) => OsFamily = osFamily;

    public void SetConnection(ConnectionState connection, DateTimeOffset now, TimeSpan grace)
    {
        if (connection == ConnectionState.Online && Connection == ConnectionState.Offline)
            ConnectedSince = now.UtcDateTime;
        if (connection == ConnectionState.Offline)
            ConnectedSince = null;
        Connection = connection;
        if (connection == ConnectionState.Online)
            LastSeenAt = now.UtcDateTime;
        Recalculate(now, grace);
    }

    public void Seen(DateTimeOffset now)
    {
        LastSeenAt = now.UtcDateTime;
        UpdatedAt = now.UtcDateTime;
    }

    public void SetLicense(LicenseStateValue state, DateTimeOffset now, TimeSpan grace)
    {
        if (state == LicenseStateValue.Unlicensed && LicenseState == LicenseStateValue.Licensed)
            UnlicensedSince = now;
        if (state == LicenseStateValue.Licensed)
            UnlicensedSince = null;
        LicenseState = state;
        Recalculate(now, grace);
    }

    public void SetOpenAlerts(int critical, int warning, DateTimeOffset now, TimeSpan grace)
    {
        OpenCritical = Math.Max(0, critical);
        OpenWarning = Math.Max(0, warning);
        Recalculate(now, grace);
    }

    public void SetMetrics(decimal? cpu, decimal? ram, decimal? disk, long? uptimeSeconds, DateTimeOffset at)
    {
        CpuPercent = cpu;
        RamPercent = ram;
        DiskPercent = disk;
        UptimeSeconds = uptimeSeconds;
        LastTelemetryAt = at.UtcDateTime;
        UpdatedAt = at.UtcDateTime;
    }

    /// <summary>Seeding: a state as of a given moment.</summary>
    public void SeedAs(ConnectionState connection, DateTimeOffset lastSeen, DateTimeOffset? connectedSince, DateTimeOffset now, TimeSpan grace)
    {
        Connection = connection;
        LastSeenAt = lastSeen.UtcDateTime;
        ConnectedSince = connectedSince?.UtcDateTime;
        Recalculate(now, grace);
    }

    public void Recalculate(DateTimeOffset now, TimeSpan grace)
    {
        Health = DeviceHealthCalculator.Calculate(Connection, OpenCritical, OpenWarning, LicenseState, UnlicensedSince, now, grace);
        UpdatedAt = now.UtcDateTime;
    }
}

/// <summary>
/// Device health (02 section 6, D14, D19): offline -> Unknown; unlicensed after the grace period -> Unknown;
/// otherwise Critical with an open critical alert, Warning with an open warning, else Healthy.
/// </summary>
public static class DeviceHealthCalculator
{
    public static DeviceHealth Calculate(
        ConnectionState connection, int openCritical, int openWarning, LicenseStateValue license, DateTimeOffset? unlicensedSince, DateTimeOffset now, TimeSpan grace)
    {
        if (connection == ConnectionState.Offline)
            return DeviceHealth.Unknown;
        if (license == LicenseStateValue.Unlicensed && unlicensedSince is { } since && now >= since.Add(grace))
            return DeviceHealth.Unknown;
        if (openCritical > 0)
            return DeviceHealth.Critical;
        return openWarning > 0 ? DeviceHealth.Warning : DeviceHealth.Healthy;
    }
}
