using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Abstractions.Authorization;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Domain.Identity;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Identity.Commands.Logout;

/// <summary>Revokes the presented refresh token's family. Always succeeds (no information about tokens leaks).</summary>
[AllowAnonymousRequest]
public sealed record LogoutCommand(string RefreshToken) : ICommand;

internal sealed class LogoutCommandValidator : AbstractValidator<LogoutCommand>
{
    public LogoutCommandValidator() => RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(200);
}

internal sealed class LogoutCommandHandler(IAppDbContext db, ITokenService tokens, AuthSessions sessions, ITenantScopeSetter scope, TimeProvider clock)
    : ICommandHandler<LogoutCommand>
{
    public async Task<Result> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        scope.RunAsSystem();
        var hash = tokens.HashRefreshToken(request.RefreshToken);
        var token = await db.Set<RefreshToken>().SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (token is not null)
            await sessions.RevokeFamilyAsync(token.FamilyId, clock.GetUtcNow(), cancellationToken);
        return Result.Success();
    }
}
