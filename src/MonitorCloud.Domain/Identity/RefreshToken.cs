using MonitorCloud.SharedKernel;

namespace MonitorCloud.Domain.Identity;

/// <summary>
/// A rotating refresh token (03 section 1). Only its HMAC is stored. Tokens issued from one sign-in share a
/// <see cref="FamilyId"/>; re-using a rotated token revokes the whole family.
/// </summary>
public sealed class RefreshToken : Entity
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);

    private RefreshToken()
    {
        TokenHash = string.Empty;
    }

    public Guid UserId { get; private set; }
    public Guid FamilyId { get; private set; }
    public string TokenHash { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public Guid? ReplacedById { get; private set; }
    public string? CreatedIp { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static RefreshToken Issue(Guid userId, Guid familyId, string tokenHash, string? ip, DateTimeOffset now, TimeSpan? lifetime = null) =>
        new()
        {
            UserId = userId,
            FamilyId = familyId,
            TokenHash = Guard.NotEmpty(tokenHash, nameof(TokenHash), 128),
            ExpiresAt = now.Add(lifetime ?? Lifetime),
            CreatedIp = ip is { Length: > 64 } ? ip[..64] : ip,
            CreatedAt = now,
        };

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    /// <summary>True when the token was already rotated or revoked: presenting it again is a re-use.</summary>
    public bool IsReuse => RevokedAt is not null;

    public void Rotate(Guid replacementId, DateTimeOffset now)
    {
        RevokedAt = now;
        ReplacedById = replacementId;
    }

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}
