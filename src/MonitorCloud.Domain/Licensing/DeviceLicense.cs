using MonitorCloud.SharedKernel;

namespace MonitorCloud.Domain.Licensing;

public enum DeviceLicenseState
{
    Unlicensed,
    Licensed,
}

/// <summary>Licence state changes of a device (enrollment, refresh, seat release), for the Devices read model.</summary>
public sealed record DeviceLicenseChangedV1(Guid DeviceId, Guid TenantId, bool Licensed, string? ReasonCode, DateTimeOffset At) : DomainEvent(At);

/// <summary>The seat of one device in the Licensing Platform (02 section 3). Keyed by the device id.</summary>
public sealed class DeviceLicense : AggregateRoot, ITenantOwned
{
    private DeviceLicense()
    {
        LicenseNumber = string.Empty;
        DeviceFingerprint = string.Empty;
    }

    public Guid DeviceId { get; private set; }
    public Guid TenantId { get; private set; }

    /// <summary>The Licensing <c>deviceId</c> (the agent fingerprint), needed to refresh and release the seat.</summary>
    public string DeviceFingerprint { get; private set; }

    public Guid LicenseId { get; private set; }
    public string LicenseNumber { get; private set; }
    public DeviceLicenseState State { get; private set; }
    public string? ReasonCode { get; private set; }
    public DateTimeOffset? UnlicensedSince { get; private set; }
    public string? Token { get; private set; }
    public string? Kid { get; private set; }
    public DateTimeOffset CheckAfter { get; private set; }
    public DateTimeOffset OfflineValidUntil { get; private set; }
    public DateTimeOffset LastCheckedAt { get; private set; }

    public static DeviceLicense Licensed(
        Guid deviceId, Guid tenantId, string fingerprint, Guid licenseId, string licenseNumber, string token, string kid, DateTimeOffset checkAfter, DateTimeOffset offlineValidUntil, DateTimeOffset now)
    {
        var license = new DeviceLicense
        {
            Id = deviceId,
            DeviceId = Guard.NotEmpty(deviceId, nameof(DeviceId)),
            TenantId = Guard.NotEmpty(tenantId, nameof(TenantId)),
            DeviceFingerprint = Guard.NotEmpty(fingerprint, nameof(DeviceFingerprint), 128),
            LicenseId = licenseId,
            LicenseNumber = Guard.NotEmpty(licenseNumber, nameof(LicenseNumber), 64),
            State = DeviceLicenseState.Licensed,
            Token = token,
            Kid = Guard.MaxLength(kid, nameof(Kid), 64),
            CheckAfter = checkAfter,
            OfflineValidUntil = offlineValidUntil,
            LastCheckedAt = now,
        };
        license.Raise(new DeviceLicenseChangedV1(deviceId, tenantId, true, null, now));
        return license;
    }

    /// <summary>A new or refreshed activation, possibly on another licence of the same customer (re-enrollment).</summary>
    public void Renew(Guid licenseId, string licenseNumber, string token, string kid, DateTimeOffset checkAfter, DateTimeOffset offlineValidUntil, DateTimeOffset now)
    {
        var wasUnlicensed = State == DeviceLicenseState.Unlicensed;
        LicenseId = licenseId;
        LicenseNumber = Guard.NotEmpty(licenseNumber, nameof(LicenseNumber), 64);
        Token = token;
        Kid = Guard.MaxLength(kid, nameof(Kid), 64);
        CheckAfter = checkAfter;
        OfflineValidUntil = offlineValidUntil;
        LastCheckedAt = now;
        State = DeviceLicenseState.Licensed;
        ReasonCode = null;
        UnlicensedSince = null;
        if (wasUnlicensed)
            Raise(new DeviceLicenseChangedV1(DeviceId, TenantId, true, null, now));
    }

    /// <summary>Seat released, licence rejected or expired. Starts the grace period of D19 once.</summary>
    public void Unlicense(string reasonCode, DateTimeOffset now)
    {
        var wasLicensed = State == DeviceLicenseState.Licensed;
        if (wasLicensed)
            UnlicensedSince = now;
        State = DeviceLicenseState.Unlicensed;
        ReasonCode = Guard.NotEmpty(reasonCode, nameof(ReasonCode), 64);
        Token = null;
        LastCheckedAt = now;
        if (wasLicensed)
            Raise(new DeviceLicenseChangedV1(DeviceId, TenantId, false, ReasonCode, now));
    }

    public void DeferCheck(DateTimeOffset nextCheck) => CheckAfter = nextCheck;

    public bool IsInGrace(DateTimeOffset now, TimeSpan grace) =>
        State == DeviceLicenseState.Unlicensed && UnlicensedSince is { } since && now < since.Add(grace);
}
