using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Abstractions.Audit;
using MonitorCloud.Application.Abstractions.Authorization;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Domain.Identity;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Identity.Commands.AcceptInvitation;

[AllowAnonymousRequest]
public sealed record AcceptInvitationCommand(string Token, string Password) : ICommand;

internal sealed class AcceptInvitationCommandValidator : AbstractValidator<AcceptInvitationCommand>
{
    public AcceptInvitationCommandValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(200);
    }
}

internal sealed class AcceptInvitationCommandHandler(
    IAppDbContext db,
    ITokenService tokens,
    IPasswordHasher hasher,
    IAuditLogger audit,
    ITenantScopeSetter scope,
    TimeProvider clock) : ICommandHandler<AcceptInvitationCommand>
{
    public async Task<Result> Handle(AcceptInvitationCommand request, CancellationToken cancellationToken)
    {
        scope.RunAsSystem();
        var hashes = tokens.InvitationTokenHashes(request.Token);
        var user = await db.Set<User>().IgnoreQueryFilters().SingleOrDefaultAsync(u => u.InvitationTokenHash != null && hashes.Contains(u.InvitationTokenHash), cancellationToken);
        if (user is null)
            return IdentityErrors.InvitationInvalid;

        if (PasswordPolicy.Check(request.Password, user.Email) is { } weak)
            return IdentityErrors.WeakPassword(weak);

        user.AcceptInvitation(user.InvitationTokenHash!, hasher.Hash(request.Password), clock.GetUtcNow());
        audit.Add("user.invitation.accepted", "User", user.Id.ToString());
        return Result.Success();
    }
}
