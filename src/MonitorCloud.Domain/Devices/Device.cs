using MonitorCloud.SharedKernel;

namespace MonitorCloud.Domain.Devices;

public enum OsFamily : byte
{
    Other = 0,
    Windows = 1,
    Linux = 2,
    MacOS = 3,
}

public enum DeviceStatus
{
    Active,
    Retired,
}

/// <summary>Agent facts reported at enrollment and on every Hello.</summary>
public sealed record AgentInfo(
    string Hostname, OsFamily OsFamily, string? OsName, string? OsVersion, string? Architecture, string? AgentVersion, int ProtocolVersion,
    string? LocalIp, string? PublicIp, string? MacAddress);

/// <summary>One machine running the agent (02 section 4).</summary>
public sealed class Device : AggregateRoot, ILocationScoped
{
    public const int NameMaxLength = 200;

    private Device()
    {
        Name = string.Empty;
        Hostname = string.Empty;
        Fingerprint = string.Empty;
    }

    public Guid TenantId { get; private set; }
    public Guid LocationId { get; private set; }
    public string Name { get; private set; }
    public string Hostname { get; private set; }

    /// <summary>The agent's stable device id; also the Licensing <c>deviceId</c>. Unique per tenant.</summary>
    public string Fingerprint { get; private set; }

    public OsFamily OsFamily { get; private set; }
    public string? OsName { get; private set; }
    public string? OsVersion { get; private set; }
    public string? Architecture { get; private set; }
    public string? AgentVersion { get; private set; }
    public int ProtocolVersion { get; private set; }
    public string? LocalIp { get; private set; }
    public string? PublicIp { get; private set; }
    public string? MacAddress { get; private set; }
    public DeviceStatus Status { get; private set; }
    public DateTimeOffset EnrolledAt { get; private set; }
    public DateTimeOffset? RetiredAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public bool IsRetired => Status == DeviceStatus.Retired;

    public static Device Enroll(Guid tenantId, Guid locationId, string fingerprint, AgentInfo info, DateTimeOffset now, Guid? id = null)
    {
        ArgumentNullException.ThrowIfNull(info);
        var device = new Device
        {
            TenantId = Guard.NotEmpty(tenantId, nameof(TenantId)),
            LocationId = Guard.NotEmpty(locationId, nameof(LocationId)),
            Fingerprint = Guard.NotEmpty(fingerprint, nameof(Fingerprint), 128),
            EnrolledAt = now,
            Status = DeviceStatus.Active,
        };
        if (id is { } fixedId)
            device.Id = Guard.NotEmpty(fixedId, nameof(Id));
        device.Apply(info);
        device.Name = Guard.NotEmpty(info.Hostname, nameof(Name), NameMaxLength);
        device.Raise(new DeviceEnrolledV1(device.Id, tenantId, locationId, device.Name, device.OsFamily, now));
        return device;
    }

    public void Rename(string name)
    {
        Guard.Against(IsRetired, DeviceErrors.Retired);
        Name = Guard.NotEmpty(name, nameof(Name), NameMaxLength);
    }

    public void MoveTo(Guid locationId, DateTimeOffset now)
    {
        Guard.Against(IsRetired, DeviceErrors.Retired);
        Guard.NotEmpty(locationId, nameof(LocationId));
        if (locationId == LocationId)
            return;
        var from = LocationId;
        LocationId = locationId;
        Raise(new DeviceMovedV1(Id, TenantId, from, locationId, now));
    }

    public void UpdateAgentInfo(AgentInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        Apply(info);
    }

    /// <summary>Releases the seat (through the event), revokes the credential, keeps the history.</summary>
    public void Retire(DateTimeOffset now)
    {
        Guard.Against(IsRetired, DeviceErrors.Retired);
        Status = DeviceStatus.Retired;
        RetiredAt = now;
        Raise(new DeviceRetiredV1(Id, TenantId, LocationId, Fingerprint, now));
    }

    /// <summary>A retired device that enrolls again comes back in its previous location (04 section 4.1).</summary>
    public void Reactivate(AgentInfo info, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(info);
        Status = DeviceStatus.Active;
        RetiredAt = null;
        Apply(info);
        Raise(new DeviceEnrolledV1(Id, TenantId, LocationId, Name, OsFamily, now));
    }

    /// <summary>The agent's stream is open again after the device was offline.</summary>
    public void CameOnline(DateTimeOffset now) => Raise(new DeviceCameOnlineV1(Id, TenantId, LocationId, now));

    /// <summary>Missed heartbeats, a closed stream or a Goodbye (<paramref name="reason"/>: the goodbye reason or "timeout").</summary>
    public void WentOffline(string reason, DateTimeOffset now) => Raise(new DeviceWentOfflineV1(Id, TenantId, LocationId, reason, now));

    /// <summary>Several sessions replaced each other: probably two machines with the same identity.</summary>
    public void SuspectClone(int replacements, DateTimeOffset now) => Raise(new DeviceCloneSuspectedV1(Id, TenantId, replacements, now));

    /// <summary>Asks the Licensing module to release the seat while the device stays (Unlicensed, D19).</summary>
    public void RequestUnlicense(DateTimeOffset now)
    {
        Guard.Against(IsRetired, DeviceErrors.Retired);
        Raise(new DeviceUnlicenseRequestedV1(Id, TenantId, Fingerprint, now));
    }

    private void Apply(AgentInfo info)
    {
        Hostname = Guard.NotEmpty(info.Hostname, nameof(Hostname), 200);
        OsFamily = info.OsFamily;
        OsName = Guard.MaxLength(info.OsName, nameof(OsName), 200);
        OsVersion = Guard.MaxLength(info.OsVersion, nameof(OsVersion), 100);
        Architecture = Guard.MaxLength(info.Architecture, nameof(Architecture), 32);
        AgentVersion = Guard.MaxLength(info.AgentVersion, nameof(AgentVersion), 32);
        ProtocolVersion = info.ProtocolVersion;
        LocalIp = Guard.MaxLength(info.LocalIp, nameof(LocalIp), 64);
        PublicIp = Guard.MaxLength(info.PublicIp, nameof(PublicIp), 64);
        MacAddress = Guard.MaxLength(info.MacAddress, nameof(MacAddress), 32);
    }

    public static OsFamily ParseOsFamily(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "windows" => OsFamily.Windows,
        "linux" => OsFamily.Linux,
        "macos" or "mac" or "osx" or "darwin" => OsFamily.MacOS,
        _ => OsFamily.Other,
    };
}

public sealed record DeviceEnrolledV1(Guid DeviceId, Guid TenantId, Guid LocationId, string Name, OsFamily OsFamily, DateTimeOffset At) : DomainEvent(At);

public sealed record DeviceMovedV1(Guid DeviceId, Guid TenantId, Guid FromLocationId, Guid ToLocationId, DateTimeOffset At) : DomainEvent(At);

public sealed record DeviceRetiredV1(Guid DeviceId, Guid TenantId, Guid LocationId, string Fingerprint, DateTimeOffset At) : DomainEvent(At);

/// <summary>A user released the device's seat but kept the device (Unlicensed, D19).</summary>
public sealed record DeviceCameOnlineV1(Guid DeviceId, Guid TenantId, Guid LocationId, DateTimeOffset At) : DomainEvent(At);

public sealed record DeviceWentOfflineV1(Guid DeviceId, Guid TenantId, Guid LocationId, string Reason, DateTimeOffset At) : DomainEvent(At);

public sealed record DeviceCloneSuspectedV1(Guid DeviceId, Guid TenantId, int Replacements, DateTimeOffset At) : DomainEvent(At);

public sealed record DeviceUnlicenseRequestedV1(Guid DeviceId, Guid TenantId, string Fingerprint, DateTimeOffset At) : DomainEvent(At);
