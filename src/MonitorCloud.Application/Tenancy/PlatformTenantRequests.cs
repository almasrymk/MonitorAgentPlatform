using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Abstractions.Audit;
using MonitorCloud.Application.Abstractions.Authorization;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Application.Common;
using MonitorCloud.Application.Devices.Contracts;
using MonitorCloud.Application.Licensing.Contracts;
using MonitorCloud.Domain.Identity;
using MonitorCloud.Domain.Tenancy;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Tenancy;

// ---------------------------------------------------------------- queries

[PlatformOnly]
[RequirePermission(Permissions.PlatformTenantsRead)]
public sealed record GetTenantsQuery(string? Search, string? Plan, string? Health, string? SubscriptionStatus, string? Status, string? Sort, int? Page, int? PageSize)
    : IQuery<PagedResult<TenantCardDto>>;

internal sealed class GetTenantsQueryHandler(IReadDbContext db, IEntitlementDirectory entitlements, IDeviceStatsDirectory devices) : IQueryHandler<GetTenantsQuery, PagedResult<TenantCardDto>>
{
    private static readonly string[] Sorts = ["name", "code", "customerSince", "status"];

    public async Task<Result<PagedResult<TenantCardDto>>> Handle(GetTenantsQuery request, CancellationToken cancellationToken)
    {
        var sort = Paging.ResolveSort(request.Sort, "name", Sorts);
        if (sort.IsFailure)
            return sort.Error!;
        var (page, pageSize) = Paging.Normalize(request.Page, request.PageSize);

        var tenants = db.Query<Tenant>();
        // Archived customers are hidden from the default list (02 section 2).
        tenants = Enum.TryParse<TenantStatus>(request.Status, true, out var status)
            ? tenants.Where(t => t.Status == status)
            : tenants.Where(t => t.Status != TenantStatus.Archived);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            tenants = tenants.Where(t => t.Name.Contains(term) || t.Code.Contains(term) || t.City.Contains(term));
        }

        // Plan, subscription status and "expiring" filters come from the Licensing cache.
        var expiringOnly = string.Equals(request.SubscriptionStatus, "Expiring", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(request.Plan) || !string.IsNullOrWhiteSpace(request.SubscriptionStatus))
        {
            var ids = await entitlements.FindTenantsAsync(request.Plan, expiringOnly ? null : request.SubscriptionStatus, expiringOnly, cancellationToken);
            tenants = tenants.Where(t => ids.Contains(t.Id));
        }

        // Health filter (07 section 5.2): critical = any critical device, warning = any warning and no critical, healthy = neither.
        var health = request.Health?.Trim().ToLowerInvariant();
        if (health is "healthy" or "warning" or "critical")
        {
            var all = await devices.ByTenantAsync(null, cancellationToken);
            var ids = all.Where(p => health switch
            {
                "critical" => p.Value.Critical > 0,
                "warning" => p.Value.Critical == 0 && p.Value.Warning > 0,
                _ => p.Value.Critical == 0 && p.Value.Warning == 0,
            }).Select(p => p.Key).ToList();
            tenants = health == "healthy"
                ? tenants.Where(t => !all.Keys.Contains(t.Id) || ids.Contains(t.Id))
                : tenants.Where(t => ids.Contains(t.Id));
        }

        tenants = (sort.Value.Key, sort.Value.Descending) switch
        {
            ("code", false) => tenants.OrderBy(t => t.Code),
            ("code", true) => tenants.OrderByDescending(t => t.Code),
            ("customerSince", false) => tenants.OrderBy(t => t.CustomerSince).ThenBy(t => t.Name),
            ("customerSince", true) => tenants.OrderByDescending(t => t.CustomerSince).ThenBy(t => t.Name),
            ("status", false) => tenants.OrderBy(t => t.Status).ThenBy(t => t.Name),
            ("status", true) => tenants.OrderByDescending(t => t.Status).ThenBy(t => t.Name),
            (_, true) => tenants.OrderByDescending(t => t.Name).ThenBy(t => t.Id),
            _ => tenants.OrderBy(t => t.Name).ThenBy(t => t.Id),
        };

        var total = await tenants.CountAsync(cancellationToken);
        var locations = db.Query<Location>();
        var items = await tenants
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new TenantCardDto(
                t.Id, t.Name, t.Code, t.Status.ToString(), t.City, t.Country, t.CustomerSince,
                locations.Count(l => l.TenantId == t.Id && !l.IsDefault),
                null, null, null, null, null, null, null, null, null, null))
            .ToListAsync(cancellationToken);

        var licensing = await entitlements.GetAsync([.. items.Select(i => i.Id)], cancellationToken);
        items = [.. items.Select(i => licensing.TryGetValue(i.Id, out var e)
            ? i with { PlanCode = e.PlanCode, PlanName = e.PlanName, SubscriptionStatus = e.SubscriptionStatus, ExpiringSoon = e.ExpiringSoon, LicensesUsed = e.ActiveSeats, LicenseLimit = e.MaxDevices, NextRenewal = e.RenewsAt }
            : i)];
        var counts = await devices.ByTenantAsync([.. items.Select(i => i.Id)], cancellationToken);
        items = [.. items.Select(i =>
        {
            var c = counts.GetValueOrDefault(i.Id) ?? DeviceCounts.Empty;
            return i with { Devices = c.Devices, Healthy = c.Healthy, Warning = c.Warning, Critical = c.Critical, HealthScore = c.HealthScore };
        })];
        return new PagedResult<TenantCardDto>(items, total, page, pageSize);
    }
}

[PlatformOnly]
[RequirePermission(Permissions.PlatformTenantsRead)]
public sealed record GetTenantsSummaryQuery : IQuery<TenantsSummaryDto>;

internal sealed class GetTenantsSummaryQueryHandler(IReadDbContext db, IEntitlementDirectory entitlements) : IQueryHandler<GetTenantsSummaryQuery, TenantsSummaryDto>
{
    public async Task<Result<TenantsSummaryDto>> Handle(GetTenantsSummaryQuery request, CancellationToken cancellationToken)
    {
        var counts = await db.Query<Tenant>()
            .Where(t => t.Status != TenantStatus.Archived)
            .GroupBy(t => t.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var active = counts.Where(c => c.Status == TenantStatus.Active).Sum(c => c.Count);
        var suspended = counts.Where(c => c.Status == TenantStatus.Suspended).Sum(c => c.Count);
        return new TenantsSummaryDto(active + suspended, active, await entitlements.CountExpiringSoonAsync(cancellationToken), suspended);
    }
}

[PlatformOnly]
[RequirePermission(Permissions.PlatformTenantsRead)]
public sealed record GetTenantQuery(Guid Id) : IQuery<TenantDto>;

internal sealed class GetTenantQueryHandler(IReadDbContext db) : IQueryHandler<GetTenantQuery, TenantDto>
{
    public async Task<Result<TenantDto>> Handle(GetTenantQuery request, CancellationToken cancellationToken)
    {
        var tenant = await db.Query<Tenant>().SingleOrDefaultAsync(t => t.Id == request.Id, cancellationToken);
        if (tenant is null)
            return TenancyErrors.TenantNotFound;
        var locations = await db.Query<Location>().CountAsync(l => l.TenantId == tenant.Id && !l.IsDefault, cancellationToken);
        return TenantMapping.ToDto(tenant, locations);
    }
}

internal static class TenantMapping
{
    public static TenantDto ToDto(Tenant t, int locations) =>
        new(t.Id, t.Name, t.Code, t.Status.ToString(), t.Country, t.City, t.TimeZone, t.CustomerSince, t.LicensingCustomerId,
            t.SuspendedAt, t.SuspensionReason, t.ArchivedAt, locations, Versioning.Encode(t.RowVersion));
}

// ---------------------------------------------------------------- commands

[PlatformOnly]
[RequirePermission(Permissions.PlatformTenantsManage)]
public sealed record CreateTenantCommand(string Name, string? Code, string Country, string City, string? TimeZone, Guid? LicensingCustomerId) : ICommand<TenantDto>;

internal sealed class CreateTenantCommandValidator : AbstractValidator<CreateTenantCommand>
{
    public CreateTenantCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Tenant.NameMaxLength);
        RuleFor(x => x.Code).MaximumLength(Tenant.CodeMaxLength).Matches("^[A-Za-z0-9_-]*$");
        RuleFor(x => x.Country).NotEmpty().MaximumLength(100);
        RuleFor(x => x.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.TimeZone).Must(TimeZones.IsValid).When(x => x.TimeZone is not null).WithMessage("Unknown time zone.");
    }
}

internal sealed class CreateTenantCommandHandler(IUnitOfWork unitOfWork, IAppDbContext db, IAuditLogger audit, TimeProvider clock) : ICommandHandler<CreateTenantCommand, TenantDto>
{
    public async Task<Result<TenantDto>> Handle(CreateTenantCommand request, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var tenant = Tenant.Create(request.Name, request.Code, request.Country, request.City, request.TimeZone, DateOnly.FromDateTime(now.UtcDateTime), request.LicensingCustomerId, now);
        if (await db.Set<Tenant>().AnyAsync(t => t.Code == tenant.Code, cancellationToken))
            return TenancyErrors.TenantCodeTaken;
        db.Set<Tenant>().Add(tenant);
        audit.Add("tenant.created", "Tenant", tenant.Id.ToString(), tenant.Name);
        // Saved here (the unit of work then has nothing left) so the returned version is the new row version.
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return TenantMapping.ToDto(tenant, 0);
    }
}

[PlatformOnly]
[RequirePermission(Permissions.PlatformTenantsManage)]
public sealed record UpdateTenantCommand(Guid Id, string Name, string Country, string City, string TimeZone, string? Version) : ICommand<TenantDto>;

internal sealed class UpdateTenantCommandValidator : AbstractValidator<UpdateTenantCommand>
{
    public UpdateTenantCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Tenant.NameMaxLength);
        RuleFor(x => x.Country).NotEmpty().MaximumLength(100);
        RuleFor(x => x.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.TimeZone).NotEmpty().Must(TimeZones.IsValid).WithMessage("Unknown time zone.");
    }
}

internal sealed class UpdateTenantCommandHandler(IUnitOfWork unitOfWork, IAppDbContext db, IAuditLogger audit) : ICommandHandler<UpdateTenantCommand, TenantDto>
{
    public async Task<Result<TenantDto>> Handle(UpdateTenantCommand request, CancellationToken cancellationToken)
    {
        var tenant = await db.Set<Tenant>().SingleOrDefaultAsync(t => t.Id == request.Id, cancellationToken);
        if (tenant is null)
            return TenancyErrors.TenantNotFound;
        db.ExpectVersion(tenant, Versioning.Decode(request.Version));
        tenant.Update(request.Name, request.Country, request.City, request.TimeZone);
        audit.Add("tenant.updated", "Tenant", tenant.Id.ToString(), tenant.Name);
        var locations = await db.Set<Location>().CountAsync(l => l.TenantId == tenant.Id && !l.IsDefault, cancellationToken);
        // Saved here (the unit of work then has nothing left) so the returned version is the new row version.
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return TenantMapping.ToDto(tenant, locations);
    }
}

public enum TenantTransition
{
    Suspend,
    Resume,
    Archive,
}

[PlatformOnly]
[RequirePermission(Permissions.PlatformTenantsManage)]
public sealed record ChangeTenantStatusCommand(Guid Id, TenantTransition Transition, string? Reason) : ICommand<TenantDto>;

internal sealed class ChangeTenantStatusCommandValidator : AbstractValidator<ChangeTenantStatusCommand>
{
    public ChangeTenantStatusCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MinimumLength(3).MaximumLength(500).When(x => x.Transition != TenantTransition.Resume);
    }
}

internal sealed class ChangeTenantStatusCommandHandler(IUnitOfWork unitOfWork, IAppDbContext db, IAuditLogger audit, TimeProvider clock) : ICommandHandler<ChangeTenantStatusCommand, TenantDto>
{
    public async Task<Result<TenantDto>> Handle(ChangeTenantStatusCommand request, CancellationToken cancellationToken)
    {
        var tenant = await db.Set<Tenant>().SingleOrDefaultAsync(t => t.Id == request.Id, cancellationToken);
        if (tenant is null)
            return TenancyErrors.TenantNotFound;
        var now = clock.GetUtcNow();
        switch (request.Transition)
        {
            case TenantTransition.Suspend:
                tenant.Suspend(request.Reason!, now);
                break;
            case TenantTransition.Resume:
                tenant.Resume(now);
                break;
            case TenantTransition.Archive:
                tenant.Archive(request.Reason!, now);
                break;
        }

        var action = request.Transition switch
        {
            TenantTransition.Suspend => "tenant.suspended",
            TenantTransition.Resume => "tenant.resumed",
            _ => "tenant.archived",
        };
        audit.Add(action, "Tenant", tenant.Id.ToString(), request.Reason);
        var locations = await db.Set<Location>().CountAsync(l => l.TenantId == tenant.Id && !l.IsDefault, cancellationToken);
        // Saved here (the unit of work then has nothing left) so the returned version is the new row version.
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return TenantMapping.ToDto(tenant, locations);
    }
}

[PlatformOnly]
[RequirePermission(Permissions.PlatformWorkspaceOpen)]
public sealed record OpenWorkspaceCommand(Guid TenantId, string Reason) : ICommand;

internal sealed class OpenWorkspaceCommandValidator : AbstractValidator<OpenWorkspaceCommand>
{
    public OpenWorkspaceCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MinimumLength(10).MaximumLength(500);
    }
}

internal sealed class OpenWorkspaceCommandHandler(IReadDbContext db, IAuditLogger audit) : ICommandHandler<OpenWorkspaceCommand>
{
    public async Task<Result> Handle(OpenWorkspaceCommand request, CancellationToken cancellationToken)
    {
        var tenant = await db.Query<Tenant>().SingleOrDefaultAsync(t => t.Id == request.TenantId, cancellationToken);
        if (tenant is null || tenant.Status == TenantStatus.Archived)
            return TenancyErrors.TenantNotFound;
        audit.Add("workspace.opened", "Tenant", tenant.Id.ToString(), request.Reason);
        return Result.Success();
    }
}

[PlatformOnly]
[RequirePermission(Permissions.PlatformWorkspaceOpen)]
public sealed record CloseWorkspaceCommand(Guid TenantId) : ICommand;

internal sealed class CloseWorkspaceCommandValidator : AbstractValidator<CloseWorkspaceCommand>
{
    public CloseWorkspaceCommandValidator() => RuleFor(x => x.TenantId).NotEmpty();
}

internal sealed class CloseWorkspaceCommandHandler(IReadDbContext db, IAuditLogger audit) : ICommandHandler<CloseWorkspaceCommand>
{
    public async Task<Result> Handle(CloseWorkspaceCommand request, CancellationToken cancellationToken)
    {
        if (!await db.Query<Tenant>().AnyAsync(t => t.Id == request.TenantId, cancellationToken))
            return TenancyErrors.TenantNotFound;
        audit.Add("workspace.closed", "Tenant", request.TenantId.ToString());
        return Result.Success();
    }
}
