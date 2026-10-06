using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Abstractions.Authorization;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Domain.Identity;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Identity.Queries.GetMe;

[AllowAuthenticatedUser]
public sealed record GetMeQuery : IQuery<UserProfileDto>;

internal sealed class GetMeQueryHandler(IReadDbContext db, ICurrentUser caller, AuthSessions sessions) : IQueryHandler<GetMeQuery, UserProfileDto>
{
    public async Task<Result<UserProfileDto>> Handle(GetMeQuery request, CancellationToken cancellationToken)
    {
        var user = await db.Query<User>().SingleOrDefaultAsync(u => u.Id == caller.UserId, cancellationToken);
        return user is null ? IdentityErrors.UserNotFound : await sessions.ProfileAsync(user, cancellationToken);
    }
}
