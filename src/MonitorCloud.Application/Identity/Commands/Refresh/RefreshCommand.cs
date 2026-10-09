using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Abstractions.Audit;
using MonitorCloud.Application.Abstractions.Authorization;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Domain.Identity;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Identity.Commands.Refresh;

[AllowAnonymousRequest]
public sealed record RefreshCommand(string RefreshToken) : ICommand<AuthResultDto>;

internal sealed class RefreshCommandValidator : AbstractValidator<RefreshCommand>
{
    public RefreshCommandValidator() => RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(200);
}

/// <summary>Rotates the refresh token. Presenting a rotated token again revokes the whole family (03 section 1).</summary>
internal sealed class RefreshCommandHandler(
    IAppDbContext db,
    IUnitOfWork unitOfWork,
    ITokenService tokens,
    AuthSessions sessions,
    IAuditLogger audit,
    ICurrentUser caller,
    ITenantScopeSetter scope,
    TimeProvider clock) : ICommandHandler<RefreshCommand, AuthResultDto>
{
    public async Task<Result<AuthResultDto>> Handle(RefreshCommand request, CancellationToken cancellationToken)
    {
        scope.RunAsSystem();
        var now = clock.GetUtcNow();
        var hashes = tokens.RefreshTokenHashes(request.RefreshToken);
        var token = await db.Set<RefreshToken>().SingleOrDefaultAsync(t => hashes.Contains(t.TokenHash), cancellationToken);
        if (token is null)
            return IdentityErrors.RefreshInvalid;

        if (token.IsReuse)
        {
            await sessions.RevokeFamilyAsync(token.FamilyId, now, cancellationToken);
            audit.Add("auth.refresh.reuse", "User", token.UserId.ToString(), $"Family {token.FamilyId} revoked", success: false);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return IdentityErrors.RefreshInvalid;
        }

        if (!token.IsActive(now))
            return IdentityErrors.RefreshInvalid;

        var user = await db.Set<User>().IgnoreQueryFilters().SingleOrDefaultAsync(u => u.Id == token.UserId, cancellationToken);
        if (user is null || user.Status != UserStatus.Active)
            return IdentityErrors.RefreshInvalid;

        if (await sessions.CheckTenantAsync(user, cancellationToken) is { } tenantError)
            return tenantError;

        return await sessions.IssueAsync(user, token.FamilyId, caller.IpAddress, now, cancellationToken, replaced: token);
    }
}
