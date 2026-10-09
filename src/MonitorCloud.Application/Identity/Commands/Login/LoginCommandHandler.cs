using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Abstractions.Audit;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Domain.Identity;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Identity.Commands.Login;

internal sealed class LoginCommandHandler(
    IAppDbContext db,
    IUnitOfWork unitOfWork,
    IPasswordHasher hasher,
    AuthSessions sessions,
    IAuditLogger audit,
    ICurrentUser caller,
    ITenantScopeSetter scope,
    TimeProvider clock) : ICommandHandler<LoginCommand, AuthResultDto>
{
    // Verified when the e-mail is unknown so both failures take the same time.
    private static string? _dummyHash;

    public async Task<Result<AuthResultDto>> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        // Sign-in happens before any tenant is known: the system scope reads and updates the user row.
        scope.RunAsSystem();
        var now = clock.GetUtcNow();
        var email = User.NormalizeEmail(request.Email);
        var user = await db.Set<User>().IgnoreQueryFilters().SingleOrDefaultAsync(u => u.Email == email, cancellationToken);

        if (user is null)
        {
            hasher.Verify(_dummyHash ??= hasher.Hash(Guid.CreateVersion7().ToString()), request.Password);
            await audit.WriteNowAsync("auth.login.failed", "User", null, $"Unknown e-mail from {caller.IpAddress}", false, cancellationToken);
            return IdentityErrors.InvalidCredentials;
        }

        if (user.IsLockedOut(now))
        {
            await audit.WriteNowAsync("auth.login.locked", "User", user.Id.ToString(), null, false, cancellationToken, Actor(user));
            return IdentityErrors.Locked(user.LockoutRemaining(now));
        }

        if (user.Status != UserStatus.Active || user.PasswordHash is null || !hasher.Verify(user.PasswordHash, request.Password))
        {
            user.RegisterFailedSignIn(now);
            audit.Add("auth.login.failed", "User", user.Id.ToString(), $"Failed attempt {user.FailedLoginCount}", success: false, actor: Actor(user));
            // The failure must persist although the command fails: save explicitly before returning.
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return user.IsLockedOut(now) ? IdentityErrors.Locked(user.LockoutRemaining(now)) : IdentityErrors.InvalidCredentials;
        }

        if (await sessions.CheckTenantAsync(user, cancellationToken) is { } tenantError)
        {
            await audit.WriteNowAsync("auth.login.failed", "User", user.Id.ToString(), "Customer suspended or archived", false, cancellationToken, Actor(user));
            return tenantError;
        }

        user.RegisterSuccessfulSignIn(now);
        audit.Add("auth.login.succeeded", "User", user.Id.ToString(), actor: Actor(user));
        return await sessions.IssueAsync(user, Guid.CreateVersion7(), caller.IpAddress, now, cancellationToken);
    }

    /// <summary>Nobody is signed in yet: the record names the user who tried.</summary>
    private static AuditActor Actor(User user) => new(user.Id, user.FullName, user.TenantId);
}
