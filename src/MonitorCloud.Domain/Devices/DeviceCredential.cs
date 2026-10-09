using MonitorCloud.SharedKernel;

namespace MonitorCloud.Domain.Devices;

/// <summary>
/// The device's 256-bit secret, stored as an HMAC with the server pepper (D11). Five failed token exchanges within
/// ten minutes block the device id for ten minutes (05 section 1.2).
/// </summary>
public sealed class DeviceCredential : Entity, ITenantOwned
{
    public const int MaxFailures = 5;
    public static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan BlockDuration = TimeSpan.FromMinutes(10);

    private DeviceCredential()
    {
        SecretHash = string.Empty;
    }

    public Guid DeviceId { get; private set; }
    public Guid TenantId { get; private set; }
    public string SecretHash { get; private set; }
    public DateTimeOffset IssuedAt { get; private set; }
    public DateTimeOffset? RotatedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public int FailedAttempts { get; private set; }
    public DateTimeOffset? FirstFailureAt { get; private set; }
    public DateTimeOffset? BlockedUntil { get; private set; }

    public bool IsRevoked => RevokedAt is not null;

    /// <summary>Stores the hash of the same secret under the current device key (after a key rotation).</summary>
    public void Rehash(string secretHash) => SecretHash = Guard.NotEmpty(secretHash, nameof(SecretHash), 128);

    public static DeviceCredential Issue(Guid deviceId, Guid tenantId, string secretHash, DateTimeOffset now) =>
        new()
        {
            Id = deviceId,
            DeviceId = Guard.NotEmpty(deviceId, nameof(DeviceId)),
            TenantId = Guard.NotEmpty(tenantId, nameof(TenantId)),
            SecretHash = Guard.NotEmpty(secretHash, nameof(SecretHash), 128),
            IssuedAt = now,
        };

    /// <summary>A new secret: the previous one stops working at once (reinstall, rotation).</summary>
    public void Replace(string secretHash, DateTimeOffset now)
    {
        SecretHash = Guard.NotEmpty(secretHash, nameof(SecretHash), 128);
        RotatedAt = now;
        RevokedAt = null;
        FailedAttempts = 0;
        FirstFailureAt = null;
        BlockedUntil = null;
    }

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;

    public bool IsBlocked(DateTimeOffset now) => BlockedUntil is { } until && until > now;

    public void RegisterFailure(DateTimeOffset now)
    {
        if (FirstFailureAt is null || now - FirstFailureAt.Value > FailureWindow)
        {
            FirstFailureAt = now;
            FailedAttempts = 0;
        }

        FailedAttempts++;
        if (FailedAttempts >= MaxFailures)
            BlockedUntil = now.Add(BlockDuration);
    }

    public void RegisterSuccess()
    {
        FailedAttempts = 0;
        FirstFailureAt = null;
    }
}
