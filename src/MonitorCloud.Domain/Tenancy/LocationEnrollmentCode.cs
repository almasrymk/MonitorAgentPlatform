using MonitorCloud.SharedKernel;

namespace MonitorCloud.Domain.Tenancy;

/// <summary>
/// A code that places enrolling devices in a location (02 section 2). The plain code <c>LOC-XXXXXX-XXXXXX</c>
/// (60 bits) is shown once; only its hash and an 8-character prefix are stored.
/// </summary>
public sealed class LocationEnrollmentCode : AggregateRoot, ILocationScoped
{
    public static readonly Error Unusable = Error.Validation("ENROLL_INVALID_LOCATION_CODE", "The location code is not valid.");

    private LocationEnrollmentCode()
    {
        CodeHash = string.Empty;
        CodePrefix = string.Empty;
    }

    public Guid TenantId { get; private set; }
    public Guid LocationId { get; private set; }
    public string CodeHash { get; private set; }
    public string CodePrefix { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public int? MaxUses { get; private set; }
    public int Uses { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static LocationEnrollmentCode Create(Guid tenantId, Guid locationId, string codeHash, string codePrefix, TimeSpan lifetime, int? maxUses, Guid? createdBy, DateTimeOffset now)
    {
        Guard.Against(lifetime <= TimeSpan.Zero || lifetime > TimeSpan.FromDays(30), Error.Validation(Guard.ValidationCode, "The code must expire within 30 days."));
        Guard.Against(maxUses is < 1, Error.Validation(Guard.ValidationCode, "Max uses must be at least 1."));
        return new LocationEnrollmentCode
        {
            TenantId = Guard.NotEmpty(tenantId, nameof(TenantId)),
            LocationId = Guard.NotEmpty(locationId, nameof(LocationId)),
            CodeHash = Guard.NotEmpty(codeHash, nameof(CodeHash), 128),
            CodePrefix = Guard.NotEmpty(codePrefix, nameof(CodePrefix), 8),
            ExpiresAt = now.Add(lifetime),
            MaxUses = maxUses,
            CreatedBy = createdBy,
            CreatedAt = now,
        };
    }

    public bool CanBeUsed(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now && (MaxUses is null || Uses < MaxUses);

    public void Use(DateTimeOffset now)
    {
        Guard.Against(!CanBeUsed(now), Unusable);
        Uses++;
    }

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}
