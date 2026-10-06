using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Abstractions.Email;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Application.Common;
using MonitorCloud.Application.Tenancy.Contracts;
using MonitorCloud.Domain.Identity;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Identity.Users;

/// <summary>Rules shared by the tenant and platform user commands.</summary>
internal sealed class UserRules(IAppDbContext db, ILocationDirectory locations, ITokenService tokens, IEmailSender email, IPortalLinks links, TimeProvider clock)
{
    public async Task<bool> EmailTakenAsync(string email, Guid? exceptUserId, CancellationToken cancellationToken)
    {
        var normalized = User.NormalizeEmail(email);
        // E-mail is unique across the whole platform, so the check must see every tenant.
        return await db.Set<User>().IgnoreQueryFilters().AnyAsync(u => u.Email == normalized && u.Id != exceptUserId, cancellationToken);
    }

    public Task<int> OtherActiveAdministratorsAsync(User user, CancellationToken cancellationToken) =>
        db.Set<User>().CountAsync(u => u.TenantId == user.TenantId && u.Id != user.Id && u.Role == Roles.Administrator && u.Status == UserStatus.Active, cancellationToken);

    public async Task<Error?> CheckLocationsAsync(Guid tenantId, IReadOnlyCollection<Guid> locationIds, CancellationToken cancellationToken)
    {
        if (locationIds.Count == 0)
            return null;
        var known = await locations.ExistingAsync(tenantId, locationIds, cancellationToken);
        return known.Count == locationIds.Distinct().Count()
            ? null
            : CommonErrors.ValidationFailed(new Dictionary<string, string[]> { ["locationIds"] = ["One or more locations do not exist."] });
    }

    /// <summary>Starts a new invitation and e-mails the link (the token itself is never stored).</summary>
    public async Task SendInvitationAsync(User user, SecretToken token, CancellationToken cancellationToken)
    {
        await email.SendAsync(new EmailMessage(
            user.Email,
            "You are invited to Monitor Agent Platform",
            $"Hello {user.FullName},\n\nYou have been invited to Monitor Agent Platform. Set your password here:\n{links.AcceptInvitation(token.Token)}\n\nThe link expires in {User.InvitationLifetime.TotalDays:0} days."),
            cancellationToken);
    }

    public SecretToken NewInvitation() => tokens.NewInvitationToken();

    public DateTimeOffset Now => clock.GetUtcNow();

    public static UserListItemDto ToDto(User u) =>
        new(u.Id, u.FullName, u.Email, u.Role, Roles.DisplayName(u.Role), Roles.PermissionSummary(u.Role), u.Status.ToString(), u.LastLoginAt, [.. u.LocationScope], Versioning.Encode(u.RowVersion));
}
