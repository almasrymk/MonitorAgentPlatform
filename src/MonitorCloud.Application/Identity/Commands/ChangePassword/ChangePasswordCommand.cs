using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Abstractions.Audit;
using MonitorCloud.Application.Abstractions.Authorization;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Domain.Identity;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Identity.Commands.ChangePassword;

[AllowAuthenticatedUser]
public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword) : ICommand;

internal sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NewPassword).NotEmpty().MaximumLength(200);
    }
}

internal sealed class ChangePasswordCommandHandler(IAppDbContext db, ICurrentUser caller, IPasswordHasher hasher, IAuditLogger audit, AuthSessions sessions, TimeProvider clock)
    : ICommandHandler<ChangePasswordCommand>
{
    public async Task<Result> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await db.Set<User>().SingleOrDefaultAsync(u => u.Id == caller.UserId, cancellationToken);
        if (user is null)
            return IdentityErrors.UserNotFound;
        if (user.PasswordHash is null || !hasher.Verify(user.PasswordHash, request.CurrentPassword))
            return IdentityErrors.CurrentPasswordWrong;
        if (PasswordPolicy.Check(request.NewPassword, user.Email) is { } weak)
            return IdentityErrors.WeakPassword(weak);

        user.ChangePassword(hasher.Hash(request.NewPassword));
        // Other sessions end: every refresh token of the user is revoked.
        var families = await db.Set<RefreshToken>().Where(t => t.UserId == user.Id && t.RevokedAt == null).Select(t => t.FamilyId).Distinct().ToListAsync(cancellationToken);
        foreach (var family in families)
            await sessions.RevokeFamilyAsync(family, clock.GetUtcNow(), cancellationToken);
        audit.Add("user.password.changed", "User", user.Id.ToString());
        return Result.Success();
    }
}
