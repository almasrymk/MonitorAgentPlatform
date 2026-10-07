using MonitorCloud.SharedKernel;

namespace MonitorCloud.Domain.Licensing;

/// <summary>
/// Every enrollment attempt, kept 90 days (02 section 3). The product key is never stored: only its 6-character
/// prefix. <see cref="TenantId"/> is null when the customer could not be identified.
/// </summary>
public sealed class EnrollmentAttempt : Entity, IOptionallyTenantOwned
{
    private EnrollmentAttempt()
    {
        KeyPrefix = string.Empty;
        DeviceFingerprint = string.Empty;
        Hostname = string.Empty;
    }

    public Guid? TenantId { get; private set; }
    public string KeyPrefix { get; private set; }
    public string DeviceFingerprint { get; private set; }
    public string Hostname { get; private set; }
    public string? Ip { get; private set; }
    public bool Succeeded { get; private set; }
    public string? ErrorCode { get; private set; }
    public DateTimeOffset At { get; private set; }

    public static EnrollmentAttempt Record(Guid? tenantId, string? productKey, string? fingerprint, string? hostname, string? ip, string? errorCode, DateTimeOffset now)
    {
        var normalized = new string((productKey ?? string.Empty).Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        return new EnrollmentAttempt
        {
            TenantId = tenantId,
            KeyPrefix = normalized.Length >= 6 ? normalized[..6] : normalized,
            DeviceFingerprint = Truncate(fingerprint, 128),
            Hostname = Truncate(hostname, 200),
            Ip = ip is null ? null : Truncate(ip, 64),
            Succeeded = errorCode is null,
            ErrorCode = errorCode is null ? null : Truncate(errorCode, 64),
            At = now,
        };
    }

    private static string Truncate(string? value, int max) => value is null ? string.Empty : value.Length <= max ? value : value[..max];
}
