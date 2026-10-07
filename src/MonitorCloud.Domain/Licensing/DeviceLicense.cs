using MonitorCloud.SharedKernel;

namespace MonitorCloud.Domain.Licensing;

public enum DeviceLicenseState
{
    Unlicensed,
    Licensed,
}

/// <summary>The seat of one device in the Licensing Platform (02 section 3). Keyed by the device id.</summary>
public sealed class DeviceLicense : Entity, ITenantOwned
{
    private DeviceLicense()
    {
        LicenseNumber = string.Empty;
    }

    public Guid DeviceId { get; private set; }
    public Guid TenantId { get; private set; }
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

    public static DeviceLicense Licensed(Guid deviceId, Guid tenantId, Guid licenseId, string licenseNumber, string token, string kid, DateTimeOffset checkAfter, DateTimeOffset offlineValidUntil, DateTimeOffset now) =>
        new()
        {
            Id = deviceId,
            DeviceId = Guard.NotEmpty(deviceId, nameof(DeviceId)),
            TenantId = Guard.NotEmpty(tenantId, nameof(TenantId)),
            LicenseId = licenseId,
            LicenseNumber = Guard.NotEmpty(licenseNumber, nameof(LicenseNumber), 64),
            State = DeviceLicenseState.Licensed,
            Token = token,
            Kid = Guard.MaxLength(kid, nameof(Kid), 64),
            CheckAfter = checkAfter,
            OfflineValidUntil = offlineValidUntil,
            LastCheckedAt = now,
        };

    public void Renew(string token, string kid, DateTimeOffset checkAfter, DateTimeOffset offlineValidUntil, DateTimeOffset now)
    {
        Token = token;
        Kid = Guard.MaxLength(kid, nameof(Kid), 64);
        CheckAfter = checkAfter;
        OfflineValidUntil = offlineValidUntil;
        LastCheckedAt = now;
        State = DeviceLicenseState.Licensed;
        ReasonCode = null;
        UnlicensedSince = null;
    }

    /// <summary>Seat released, licence rejected or expired. Starts the grace period of D19 once.</summary>
    public void Unlicense(string reasonCode, DateTimeOffset now)
    {
        if (State == DeviceLicenseState.Licensed)
            UnlicensedSince = now;
        State = DeviceLicenseState.Unlicensed;
        ReasonCode = Guard.NotEmpty(reasonCode, nameof(ReasonCode), 64);
        Token = null;
        LastCheckedAt = now;
    }

    public void DeferCheck(DateTimeOffset nextCheck) => CheckAfter = nextCheck;

    public bool IsInGrace(DateTimeOffset now, TimeSpan grace) =>
        State == DeviceLicenseState.Unlicensed && UnlicensedSince is { } since && now < since.Add(grace);
}
