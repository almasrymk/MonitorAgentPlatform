using MonitorCloud.SharedKernel;

namespace MonitorCloud.Domain.Configuration;

/// <summary>
/// The device configuration (02 section 8): thresholds, telemetry and features as a JSON document, versioned. Monitor
/// points are part of the document sent to the agent but are kept by the Monitoring module; a change there bumps
/// this version too. Every change raises <see cref="DeviceConfigurationChangedV1"/>, which pushes <c>ConfigUpdate</c>.
/// </summary>
public sealed class DeviceConfiguration : AggregateRoot, ITenantOwned
{
    private DeviceConfiguration()
    {
        DocumentJson = "{}";
    }

    public Guid DeviceId { get; private set; }
    public Guid TenantId { get; private set; }
    public int Version { get; private set; }
    public string DocumentJson { get; private set; }
    public Guid? UpdatedByUserId { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static DeviceConfiguration Create(Guid deviceId, Guid tenantId, string documentJson, DateTimeOffset now) =>
        new()
        {
            Id = deviceId,
            DeviceId = Guard.NotEmpty(deviceId, nameof(DeviceId)),
            TenantId = Guard.NotEmpty(tenantId, nameof(TenantId)),
            Version = 1,
            DocumentJson = Guard.NotEmpty(documentJson, nameof(DocumentJson), 64_000),
            UpdatedAt = now,
        };

    /// <summary>A new document from the portal: the version goes up by one.</summary>
    public void Update(string documentJson, Guid? userId, DateTimeOffset now)
    {
        DocumentJson = Guard.NotEmpty(documentJson, nameof(DocumentJson), 64_000);
        Bump(userId, now);
    }

    /// <summary>Something else in the document changed (monitor points): the version goes up by one.</summary>
    public void Bump(Guid? userId, DateTimeOffset now)
    {
        Version++;
        UpdatedByUserId = userId;
        UpdatedAt = now;
        Raise(new DeviceConfigurationChangedV1(DeviceId, TenantId, Version, now));
    }
}

/// <summary>What the agent answered to the last <c>ConfigUpdate</c> (02 section 8).</summary>
public sealed class DeviceConfigurationAck : AggregateRoot, ITenantOwned
{
    private DeviceConfigurationAck()
    {
    }

    public Guid DeviceId { get; private set; }
    public Guid TenantId { get; private set; }

    /// <summary>The last version the agent applied successfully.</summary>
    public int AppliedVersion { get; private set; }

    public DateTimeOffset AppliedAt { get; private set; }

    /// <summary>The version of the last rejected document, and why (null after a success).</summary>
    public int? RejectedVersion { get; private set; }

    public string? Error { get; private set; }

    public static DeviceConfigurationAck Create(Guid deviceId, Guid tenantId) => new() { Id = deviceId, DeviceId = deviceId, TenantId = tenantId };

    public void Applied(int version, DateTimeOffset at)
    {
        if (version >= AppliedVersion)
        {
            AppliedVersion = version;
            AppliedAt = at;
        }

        RejectedVersion = null;
        Error = null;
        Raise(new DeviceConfigurationAppliedV1(DeviceId, TenantId, version, true, null, at));
    }

    public void Rejected(int version, string? error, DateTimeOffset at)
    {
        RejectedVersion = version;
        Error = string.IsNullOrWhiteSpace(error) ? "Rejected by the agent." : error.Length <= 500 ? error : error[..500];
        AppliedAt = AppliedVersion == 0 ? at : AppliedAt;
        Raise(new DeviceConfigurationAppliedV1(DeviceId, TenantId, version, false, Error, at));
    }
}

/// <summary>Default thresholds for the configuration of new devices (Settings > Monitoring).</summary>
public sealed class TenantConfigurationDefaults : Entity, ITenantOwned
{
    private TenantConfigurationDefaults()
    {
        DocumentJson = "{}";
    }

    public Guid TenantId { get; private set; }
    public string DocumentJson { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static TenantConfigurationDefaults Create(Guid tenantId, string documentJson, DateTimeOffset now) =>
        new() { Id = tenantId, TenantId = Guard.NotEmpty(tenantId, nameof(TenantId)), DocumentJson = Guard.NotEmpty(documentJson, nameof(DocumentJson), 64_000), UpdatedAt = now };

    public void Update(string documentJson, DateTimeOffset now)
    {
        DocumentJson = Guard.NotEmpty(documentJson, nameof(DocumentJson), 64_000);
        UpdatedAt = now;
    }
}

public sealed record DeviceConfigurationChangedV1(Guid DeviceId, Guid TenantId, int Version, DateTimeOffset At) : DomainEvent(At);

public sealed record DeviceConfigurationAppliedV1(Guid DeviceId, Guid TenantId, int Version, bool Success, string? Error, DateTimeOffset At) : DomainEvent(At);
