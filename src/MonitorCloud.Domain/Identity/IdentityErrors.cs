using MonitorCloud.SharedKernel;

namespace MonitorCloud.Domain.Identity;

public static class IdentityErrors
{
    public static readonly Error InvalidCredentials = Error.Unauthorized("AUTH_INVALID_CREDENTIALS", "The e-mail or password is incorrect.");
    public static readonly Error TenantSuspended = Error.Forbidden("AUTH_TENANT_SUSPENDED", "This customer account is suspended.");
    public static readonly Error RefreshInvalid = Error.Unauthorized("AUTH_REFRESH_INVALID", "The session has expired. Sign in again.");
    public static readonly Error InvitationInvalid = Error.Validation("AUTH_INVITATION_INVALID", "The invitation is invalid or has expired.");
    public static readonly Error CurrentPasswordWrong = Error.Validation("AUTH_PASSWORD_INCORRECT", "The current password is incorrect.");
    public static readonly Error UserNotFound = Error.NotFound("USER_NOT_FOUND", "User not found.");
    public static readonly Error EmailTaken = Error.Conflict("USER_EMAIL_TAKEN", "A user with this e-mail already exists.");
    public static readonly Error LastAdministrator = Error.Conflict("USER_LAST_ADMIN", "The last active administrator cannot be removed or demoted.");
    public static readonly Error InvalidTransition = Error.Conflict("USER_INVALID_TRANSITION", "The user cannot change to that status.");
    public static readonly Error RoleNotAllowed = Error.Validation("USER_ROLE_NOT_ALLOWED", "The role is not allowed for this user.");

    public static Error Locked(TimeSpan remaining) =>
        Error.Locked("AUTH_LOCKED", "Too many failed sign-ins. Try again later.")
            .With("retryAfterSeconds", (int)Math.Ceiling(Math.Max(1, remaining.TotalSeconds)));

    public static Error WeakPassword(string reason) => Error.Validation("AUTH_PASSWORD_WEAK", reason);
}
