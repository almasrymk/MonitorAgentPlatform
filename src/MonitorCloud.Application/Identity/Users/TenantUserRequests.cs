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

// ---------------------------------------------------------------- queries

[RequirePermission(Permissions.UsersManage)]
public sealed record GetUsersQuery(string? Role, string? Status, string? Search, string? Sort, int? Page, int? PageSize) : IQuery<PagedResult<UserListItemDto>>;

internal sealed class GetUsersQueryHandler(IReadDbContext db) : IQueryHandler<GetUsersQuery, PagedResult<UserListItemDto>>
{
    public static readonly string[] Sorts = ["name", "email", "role", "status", "lastLogin"];

    public async Task<Result<PagedResult<UserListItemDto>>> Handle(GetUsersQuery request, CancellationToken cancellationToken)
    {
        var sort = Paging.ResolveSort(request.Sort, "name", Sorts);
        if (sort.IsFailure)
            return sort.Error!;
        var (page, pageSize) = Paging.Normalize(request.Page, request.PageSize);

        // Tenant users only: the query filter limits the rows to the scope's tenant.
        var query = db.Query<User>().Where(u => u.TenantId != null);
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

[RequirePermission(Permissions.UsersManage)]
public sealed record GetRolesQuery : IQuery<IReadOnlyList<RoleDto>>;

internal sealed class GetRolesQueryHandler : IQueryHandler<GetRolesQuery, IReadOnlyList<RoleDto>>
{
    public Task<Result<IReadOnlyList<RoleDto>>> Handle(GetRolesQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<RoleDto> roles = Roles.Tenant
            .Select(r => new RoleDto(r, Roles.DisplayName(r), Roles.PermissionSummary(r), [.. Roles.PermissionsOf(r).Order(StringComparer.Ordinal)]))
            .ToList();
        return Task.FromResult(Result.Success(roles));
    }
}

internal static class UserQueries
{
    public static async Task<PagedResult<UserListItemDto>> PageAsync(IQueryable<User> query, (string Key, bool Descending) sort, int page, int pageSize, CancellationToken cancellationToken)
    {
        query = (sort.Key, sort.Descending) switch
        {
            ("email", false) => query.OrderBy(u => u.Email),
            ("email", true) => query.OrderByDescending(u => u.Email),
            ("role", false) => query.OrderBy(u => u.Role).ThenBy(u => u.FullName),
            ("role", true) => query.OrderByDescending(u => u.Role).ThenBy(u => u.FullName),
            ("status", false) => query.OrderBy(u => u.Status).ThenBy(u => u.FullName),
            ("status", true) => query.OrderByDescending(u => u.Status).ThenBy(u => u.FullName),
            ("lastLogin", false) => query.OrderBy(u => u.LastLoginAt).ThenBy(u => u.Id),
            ("lastLogin", true) => query.OrderByDescending(u => u.LastLoginAt).ThenBy(u => u.Id),
            (_, true) => query.OrderByDescending(u => u.FullName).ThenBy(u => u.Id),
            _ => query.OrderBy(u => u.FullName).ThenBy(u => u.Id),
        };

        var total = await query.CountAsync(cancellationToken);
        var users = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PagedResult<UserListItemDto>(users.Select(UserRules.ToDto).ToList(), total, page, pageSize);
    }
}

// ---------------------------------------------------------------- commands

[RequirePermission(Permissions.UsersManage)]
public sealed record InviteUserCommand(string FullName, string Email, string Role, IReadOnlyList<Guid>? LocationIds) : ICommand<UserListItemDto>;

internal sealed class InviteUserCommandValidator : AbstractValidator<InviteUserCommand>
{
    public InviteUserCommandValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(User.FullNameMaxLength);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(User.EmailMaxLength);
        RuleFor(x => x.Role).Must(Roles.IsTenantRole).WithMessage("Role must be Administrator, ITManager, Technician or ReportViewer.");
    }
}

internal sealed class InviteUserCommandHandler(IUnitOfWork unitOfWork, IAppDbContext db, ITenantContext scope, UserRules rules, IAuditLogger audit)
    : ICommandHandler<InviteUserCommand, UserListItemDto>
{
    public async Task<Result<UserListItemDto>> Handle(InviteUserCommand request, CancellationToken cancellationToken)
    {
        var tenantId = scope.TenantId!.Value;
        if (await rules.EmailTakenAsync(request.Email, null, cancellationToken))
            return IdentityErrors.EmailTaken;
        var locations = request.LocationIds ?? [];
        if (await rules.CheckLocationsAsync(tenantId, locations, cancellationToken) is { } locationError)
            return locationError;

        var invitation = rules.NewInvitation();
        var user = User.Invite(tenantId, request.Email, request.FullName, request.Role, locations, invitation.Hash, rules.Now);
        db.Set<User>().Add(user);
        audit.Add("user.invited", "User", user.Id.ToString(), $"{user.Email} as {user.Role}");
        await rules.SendInvitationAsync(user, invitation, cancellationToken);
        // Saved here (the unit of work then has nothing left) so the returned version is the new row version.
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return UserRules.ToDto(user);
    }
}

[RequirePermission(Permissions.UsersManage)]
public sealed record UpdateUserCommand(Guid Id, string FullName, string Role, IReadOnlyList<Guid>? LocationIds, string? Version) : ICommand<UserListItemDto>;

internal sealed class UpdateUserCommandValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserCommandValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(User.FullNameMaxLength);
        RuleFor(x => x.Role).Must(Roles.IsTenantRole).WithMessage("Role must be Administrator, ITManager, Technician or ReportViewer.");
    }
}

internal sealed class UpdateUserCommandHandler(IUnitOfWork unitOfWork, IAppDbContext db, UserRules rules, IAuditLogger audit) : ICommandHandler<UpdateUserCommand, UserListItemDto>
{
    public async Task<Result<UserListItemDto>> Handle(UpdateUserCommand request, CancellationToken cancellationToken)
    {
        var user = await db.Set<User>().SingleOrDefaultAsync(u => u.Id == request.Id && u.TenantId != null, cancellationToken);
        if (user is null)
            return IdentityErrors.UserNotFound;
        db.ExpectVersion(user, Versioning.Decode(request.Version));

        var locations = request.LocationIds ?? [];
        if (await rules.CheckLocationsAsync(user.TenantId!.Value, locations, cancellationToken) is { } locationError)
            return locationError;

        var roleChanged = user.Role != request.Role;
        user.UpdateProfile(request.FullName);
        user.ChangeRole(request.Role, locations, await rules.OtherActiveAdministratorsAsync(user, cancellationToken));
        audit.Add(roleChanged ? "user.role.changed" : "user.updated", "User", user.Id.ToString(), $"Role {user.Role}");
        // Saved here (the unit of work then has nothing left) so the returned version is the new row version.
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return UserRules.ToDto(user);
    }
}

[RequirePermission(Permissions.UsersManage)]
public sealed record ActivateUserCommand(Guid Id) : ICommand<UserListItemDto>;

internal sealed class ActivateUserCommandValidator : AbstractValidator<ActivateUserCommand>
{
    public ActivateUserCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

internal sealed class ActivateUserCommandHandler(IUnitOfWork unitOfWork, IAppDbContext db, IAuditLogger audit) : ICommandHandler<ActivateUserCommand, UserListItemDto>
{
    public async Task<Result<UserListItemDto>> Handle(ActivateUserCommand request, CancellationToken cancellationToken)
    {
        var user = await db.Set<User>().SingleOrDefaultAsync(u => u.Id == request.Id && u.TenantId != null, cancellationToken);
        if (user is null)
            return IdentityErrors.UserNotFound;
        user.Activate();
        audit.Add("user.activated", "User", user.Id.ToString());
        // Saved here (the unit of work then has nothing left) so the returned version is the new row version.
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return UserRules.ToDto(user);
    }
}

[RequirePermission(Permissions.UsersManage)]
public sealed record DeactivateUserCommand(Guid Id) : ICommand<UserListItemDto>;

internal sealed class DeactivateUserCommandValidator : AbstractValidator<DeactivateUserCommand>
{
    public DeactivateUserCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

internal sealed class DeactivateUserCommandHandler(IUnitOfWork unitOfWork, IAppDbContext db, UserRules rules, IAuditLogger audit, TimeProvider clock)
    : ICommandHandler<DeactivateUserCommand, UserListItemDto>
{
    public async Task<Result<UserListItemDto>> Handle(DeactivateUserCommand request, CancellationToken cancellationToken)
    {
        var user = await db.Set<User>().SingleOrDefaultAsync(u => u.Id == request.Id && u.TenantId != null, cancellationToken);
        if (user is null)
            return IdentityErrors.UserNotFound;
        user.Deactivate(await rules.OtherActiveAdministratorsAsync(user, cancellationToken));
        var tokens = await db.Set<RefreshToken>().Where(t => t.UserId == user.Id && t.RevokedAt == null).ToListAsync(cancellationToken);
        foreach (var token in tokens)
            token.Revoke(clock.GetUtcNow());
        audit.Add("user.deactivated", "User", user.Id.ToString());
        // Saved here (the unit of work then has nothing left) so the returned version is the new row version.
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return UserRules.ToDto(user);
    }
}

[RequirePermission(Permissions.UsersManage)]
public sealed record ResendInvitationCommand(Guid Id) : ICommand;

internal sealed class ResendInvitationCommandValidator : AbstractValidator<ResendInvitationCommand>
{
    public ResendInvitationCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

internal sealed class ResendInvitationCommandHandler(IAppDbContext db, UserRules rules, IAuditLogger audit) : ICommandHandler<ResendInvitationCommand>
{
    public async Task<Result> Handle(ResendInvitationCommand request, CancellationToken cancellationToken)
    {
        var user = await db.Set<User>().SingleOrDefaultAsync(u => u.Id == request.Id && u.TenantId != null, cancellationToken);
        if (user is null)
            return IdentityErrors.UserNotFound;
        var invitation = rules.NewInvitation();
        user.SetInvitation(invitation.Hash, rules.Now);
        audit.Add("user.invitation.resent", "User", user.Id.ToString());
        await rules.SendInvitationAsync(user, invitation, cancellationToken);
        return Result.Success();
    }
}
