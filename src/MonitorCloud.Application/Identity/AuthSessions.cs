using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Application.Tenancy.Contracts;
using MonitorCloud.Domain.Identity;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Identity;

/// <summary>Shared by sign-in and refresh: tenant checks, token pair creation and the profile.</summary>
internal sealed class AuthSessions(IAppDbContext db, ITokenService tokens, ITenantDirectory tenants)
{
    public async Task<Error?> CheckTenantAsync(User user, CancellationToken cancellationToken)
    {
        if (user.TenantId is not { } tenantId)
            return null;
        var tenant = await tenants.FindAsync(tenantId, cancellationToken);
        return tenant is { IsActive: true } ? null : IdentityErrors.TenantSuspended;
    }

    public async Task<AuthResultDto> IssueAsync(User user, Guid familyId, string? ip, DateTimeOffset now, CancellationToken cancellationToken, RefreshToken? replaced = null)
    {
        var access = tokens.CreateAccessToken(user, now);
        var refresh = tokens.NewRefreshToken();
        var token = RefreshToken.Issue(user.Id, familyId, refresh.Hash, ip, now);
        db.Set<RefreshToken>().Add(token);
        replaced?.Rotate(token.Id, now);
        var profile = await ProfileAsync(user, cancellationToken);
        return new AuthResultDto(access.Token, access.ExpiresAt, refresh.Token, profile);
    }

    public async Task<UserProfileDto> ProfileAsync(User user, CancellationToken cancellationToken)
    {
        string? tenantName = null;
        if (user.TenantId is { } tenantId)
            tenantName = (await tenants.FindAsync(tenantId, cancellationToken))?.Name;
        return new UserProfileDto(
            user.Id,
            user.FullName,
            user.Email,
            user.Role,
            user.TenantId,
            tenantName,
            [.. Roles.PermissionsOf(user.Role).Order(StringComparer.Ordinal)],
            user.PreferredLanguage,
            [.. user.LocationScope]);
    }

    public async Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var family = await db.Set<RefreshToken>().Where(t => t.FamilyId == familyId && t.RevokedAt == null).ToListAsync(cancellationToken);
        foreach (var token in family)
            token.Revoke(now);
    }
}
