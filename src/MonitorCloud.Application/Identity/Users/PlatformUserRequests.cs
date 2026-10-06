using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Abstractions.Audit;
using MonitorCloud.Application.Abstractions.Authorization;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Application.Common;
using MonitorCloud.Domain.Identity;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Identity.Users;

[PlatformOnly]
[RequirePermission(Permissions.PlatformUsersManage)]
public sealed record GetPlatformUsersQuery(string? Role, string? Status, string? Search, string? Sort, int? Page, int? PageSize) : IQuery<PagedResult<UserListItemDto>>;

internal sealed class GetPlatformUsersQueryHandler(IReadDbContext db) : IQueryHandler<GetPlatformUsersQuery, PagedResult<UserListItemDto>>
{
    public async Task<Result<PagedResult<UserListItemDto>>> Handle(GetPlatformUsersQuery request, CancellationToken cancellationToken)
    {
        var sort = Paging.ResolveSort(request.Sort, "name", GetUsersQueryHandler.Sorts);
        if (sort.IsFailure)
            return sort.Error!;
        var (page, pageSize) = Paging.Normalize(request.Page, request.PageSize);

        var query = db.Query<User>().Where(u => u.TenantId == null);
        if (!string.IsNullOrWhiteSpace(request.Role))
            query = query.Where(u => u.Role == request.Role);
        if (Enum.TryParse<UserStatus>(request.Status, true, out var status))
            query = query.Where(u => u.Status == status);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(u => u.FullName.Contains(term) || u.Email.Contains(term));
        }

        return await UserQueries.PageAsync(query, sort.Value, page, pageSize, cancellationToken);
    }
}

[PlatformOnly]
[RequirePermission(Permissions.PlatformUsersManage)]
public sealed record CreatePlatformUserCommand(string FullName, string Email, string Role, string Password) : ICommand<UserListItemDto>;

internal sealed class CreatePlatformUserCommandValidator : AbstractValidator<CreatePlatformUserCommand>
{
    public CreatePlatformUserCommandValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(User.FullNameMaxLength);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(User.EmailMaxLength);
        RuleFor(x => x.Role).Must(Roles.IsPlatformRole).WithMessage("Role must be PlatformAdmin or PlatformSupport.");
        RuleFor(x => x.Password).Custom((password, context) =>
        {
            if (PasswordPolicy.Check(password, context.InstanceToValidate.Email) is { } weak)
                context.AddFailure(weak);
        });
    }
}

internal sealed class CreatePlatformUserCommandHandler(IUnitOfWork unitOfWork, IAppDbContext db, UserRules rules, IPasswordHasher hasher, IAuditLogger audit)
    : ICommandHandler<CreatePlatformUserCommand, UserListItemDto>
{
    public async Task<Result<UserListItemDto>> Handle(CreatePlatformUserCommand request, CancellationToken cancellationToken)
    {
        if (await rules.EmailTakenAsync(request.Email, null, cancellationToken))
            return IdentityErrors.EmailTaken;
        var user = User.CreatePlatformUser(request.Email, request.FullName, request.Role, hasher.Hash(request.Password), rules.Now);
        db.Set<User>().Add(user);
        audit.Add("platform.user.created", "User", user.Id.ToString(), $"{user.Email} as {user.Role}");
        // Saved here (the unit of work then has nothing left) so the returned version is the new row version.
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return UserRules.ToDto(user);
    }
}

[PlatformOnly]
[RequirePermission(Permissions.PlatformUsersManage)]
public sealed record UpdatePlatformUserCommand(Guid Id, string FullName, string Role, string? Version) : ICommand<UserListItemDto>;

internal sealed class UpdatePlatformUserCommandValidator : AbstractValidator<UpdatePlatformUserCommand>
{
    public UpdatePlatformUserCommandValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(User.FullNameMaxLength);
        RuleFor(x => x.Role).Must(Roles.IsPlatformRole).WithMessage("Role must be PlatformAdmin or PlatformSupport.");
    }
}

internal sealed class UpdatePlatformUserCommandHandler(IUnitOfWork unitOfWork, IAppDbContext db, IAuditLogger audit, ICurrentUser caller) : ICommandHandler<UpdatePlatformUserCommand, UserListItemDto>
{
    public async Task<Result<UserListItemDto>> Handle(UpdatePlatformUserCommand request, CancellationToken cancellationToken)
    {
        var user = await db.Set<User>().SingleOrDefaultAsync(u => u.Id == request.Id && u.TenantId == null, cancellationToken);
        if (user is null)
            return IdentityErrors.UserNotFound;
        db.ExpectVersion(user, Versioning.Decode(request.Version));
        if (user.Id == caller.UserId && request.Role != user.Role)
            return IdentityErrors.RoleNotAllowed;
        user.UpdateProfile(request.FullName);
        user.ChangeRole(request.Role, [], otherActiveAdministrators: 1);
        audit.Add("platform.user.updated", "User", user.Id.ToString(), $"Role {user.Role}");
        // Saved here (the unit of work then has nothing left) so the returned version is the new row version.
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return UserRules.ToDto(user);
    }
}

[PlatformOnly]
[RequirePermission(Permissions.PlatformUsersManage)]
public sealed record SetPlatformUserActiveCommand(Guid Id, bool Active) : ICommand<UserListItemDto>;

internal sealed class SetPlatformUserActiveCommandValidator : AbstractValidator<SetPlatformUserActiveCommand>
{
    public SetPlatformUserActiveCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

internal sealed class SetPlatformUserActiveCommandHandler(IUnitOfWork unitOfWork, IAppDbContext db, IAuditLogger audit, ICurrentUser caller, TimeProvider clock)
    : ICommandHandler<SetPlatformUserActiveCommand, UserListItemDto>
{
    public async Task<Result<UserListItemDto>> Handle(SetPlatformUserActiveCommand request, CancellationToken cancellationToken)
    {
        var user = await db.Set<User>().SingleOrDefaultAsync(u => u.Id == request.Id && u.TenantId == null, cancellationToken);
        if (user is null)
            return IdentityErrors.UserNotFound;
        if (request.Active)
        {
            user.Activate();
        }
        else
        {
            if (user.Id == caller.UserId)
                return IdentityErrors.InvalidTransition;
            user.Deactivate(otherActiveAdministrators: 1);
            var tokens = await db.Set<RefreshToken>().Where(t => t.UserId == user.Id && t.RevokedAt == null).ToListAsync(cancellationToken);
            foreach (var token in tokens)
                token.Revoke(clock.GetUtcNow());
        }

        audit.Add(request.Active ? "platform.user.activated" : "platform.user.deactivated", "User", user.Id.ToString());
        // Saved here (the unit of work then has nothing left) so the returned version is the new row version.
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return UserRules.ToDto(user);
    }
}

[PlatformOnly]
[RequirePermission(Permissions.PlatformUsersManage)]
public sealed record ResetPlatformUserPasswordCommand(Guid Id, string NewPassword) : ICommand;

internal sealed class ResetPlatformUserPasswordCommandValidator : AbstractValidator<ResetPlatformUserPasswordCommand>
{
    public ResetPlatformUserPasswordCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MaximumLength(200);
    }
}

internal sealed class ResetPlatformUserPasswordCommandHandler(IAppDbContext db, IPasswordHasher hasher, IAuditLogger audit, TimeProvider clock)
    : ICommandHandler<ResetPlatformUserPasswordCommand>
{
    public async Task<Result> Handle(ResetPlatformUserPasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await db.Set<User>().SingleOrDefaultAsync(u => u.Id == request.Id && u.TenantId == null, cancellationToken);
        if (user is null)
            return IdentityErrors.UserNotFound;
        if (PasswordPolicy.Check(request.NewPassword, user.Email) is { } weak)
            return IdentityErrors.WeakPassword(weak);
        user.ChangePassword(hasher.Hash(request.NewPassword));
        user.RegisterSuccessfulSignIn(user.LastLoginAt ?? clock.GetUtcNow());
        var tokens = await db.Set<RefreshToken>().Where(t => t.UserId == user.Id && t.RevokedAt == null).ToListAsync(cancellationToken);
        foreach (var token in tokens)
            token.Revoke(clock.GetUtcNow());
        audit.Add("platform.user.password.reset", "User", user.Id.ToString());
        return Result.Success();
    }
}
