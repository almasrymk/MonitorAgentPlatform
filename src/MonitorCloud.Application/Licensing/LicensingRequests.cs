using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MonitorCloud.Application.Abstractions.Audit;
using MonitorCloud.Application.Abstractions.Authorization;
using MonitorCloud.Application.Abstractions.Caching;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Application.Licensing.Contracts;
using MonitorCloud.Domain.Identity;
using MonitorCloud.Domain.Licensing;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Licensing;

public sealed record SubscriptionDto(
    string? PlanCode,
    string? PlanName,
    string Status,
    IReadOnlyList<string> Features,
    DateTimeOffset? StartsAt,
    DateTimeOffset? RenewsAt,
    int? DaysRemaining,
    bool ExpiringSoon,
    int DevicesUsed,
    int? DeviceLimit,
    int LicensedDevices,
    int UnlicensedDevices,
    IReadOnlyList<OsUsageDto> UsageByOs,
    DateTimeOffset? SyncedAt,
    bool Stale);

[RequirePermission(Permissions.SubscriptionRead)]
public sealed record GetSubscriptionQuery : IQuery<SubscriptionDto>;

internal sealed class GetSubscriptionQueryHandler(IReadDbContext db, ITenantContext scope, IDeviceLicenseStats devices, IOptions<LicensingSettings> settings, TimeProvider clock)
    : IQueryHandler<GetSubscriptionQuery, SubscriptionDto>
{
    public async Task<Result<SubscriptionDto>> Handle(GetSubscriptionQuery request, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var entitlement = await db.Query<TenantEntitlement>().SingleOrDefaultAsync(e => e.TenantId == scope.TenantId, cancellationToken);
        var (licensed, unlicensed, byOs) = await devices.GetAsync(cancellationToken);
        if (entitlement is null)
            return new SubscriptionDto(null, null, nameof(SubscriptionStatus.None), [], null, null, null, false, 0, 0, licensed, unlicensed, byOs, null, true);

        var days = entitlement.RenewsAt is { } end ? (int?)Math.Max(0, (int)Math.Ceiling((end - now).TotalDays)) : null;
        var stale = entitlement.SyncedAt is null || now - entitlement.SyncedAt.Value > TimeSpan.FromMinutes(settings.Value.EntitlementStaleAfterMinutes);
        return new SubscriptionDto(
            entitlement.PlanCode, entitlement.PlanName, entitlement.SubscriptionStatus.ToString(), entitlement.Features,
            entitlement.StartsAt, entitlement.RenewsAt, days, entitlement.IsExpiringSoon(now, TimeSpan.FromDays(settings.Value.ExpiringSoonDays)),
            entitlement.ActiveSeats, entitlement.MaxDevices, licensed, unlicensed, byOs, entitlement.SyncedAt, stale);
    }
}

public sealed record PlanDto(string Code, string Name, int Version, IReadOnlyList<string> Features, int? DeviceLimit, int? DurationDays, decimal Price, string Currency, int Customers);

[PlatformOnly]
[RequirePermission(Permissions.PlatformPlansRead)]
public sealed record GetPlansQuery : IQuery<IReadOnlyList<PlanDto>>, ICacheableQuery
{
    public string CacheKey => "plans";
    public TimeSpan CacheDuration => TimeSpan.FromSeconds(60);
}

internal sealed class GetPlansQueryHandler(ILicensingGateway gateway, IReadDbContext db) : IQueryHandler<GetPlansQuery, IReadOnlyList<PlanDto>>
{
    public async Task<Result<IReadOnlyList<PlanDto>>> Handle(GetPlansQuery request, CancellationToken cancellationToken)
    {
        var plans = await gateway.ListPlansAsync(cancellationToken);
        if (plans.IsFailure)
            return plans.Error!;

        var customers = await db.Query<TenantEntitlement>()
            .Where(e => e.PlanCode != null)
            .GroupBy(e => e.PlanCode!)
            .Select(g => new { Plan = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Plan, x => x.Count, cancellationToken);

        IReadOnlyList<PlanDto> result = plans.Value
            .GroupBy(p => p.Code)
            .Select(g => g.OrderByDescending(p => p.Version).First())
            .OrderBy(p => p.MaxActivations ?? int.MaxValue)
            .Select(p => new PlanDto(p.Code, p.Name, p.Version, p.Features, p.MaxActivations, p.DurationDays, p.Price.Amount, p.Price.Currency, customers.GetValueOrDefault(p.Code)))
            .ToList();
        return Result.Success(result);
    }
}

public sealed record LicensingStatusDto(
    string Mode, DateTimeOffset? LastSuccessAt, DateTimeOffset? LastAttemptAt, DateTimeOffset? LastFullReconcileAt, string? LastError, int ConsecutiveFailures, bool Degraded, string? PortalUrl);

[PlatformOnly]
[RequirePermission(Permissions.PlatformDashboardRead)]
public sealed record GetLicensingStatusQuery : IQuery<LicensingStatusDto>;

internal sealed class GetLicensingStatusQueryHandler(IReadDbContext db, IOptions<LicensingSettings> settings) : IQueryHandler<GetLicensingStatusQuery, LicensingStatusDto>
{
    public const int DegradedAfterFailures = 5;

    public async Task<Result<LicensingStatusDto>> Handle(GetLicensingStatusQuery request, CancellationToken cancellationToken)
    {
        var state = await db.Query<LicensingSyncState>().SingleOrDefaultAsync(cancellationToken);
        return new LicensingStatusDto(
            settings.Value.IsLive ? "Live" : "Fake",
            state?.LastSuccessAt, state?.LastAttemptAt, state?.LastFullReconcileAt, state?.LastError, state?.ConsecutiveFailures ?? 0,
            (state?.ConsecutiveFailures ?? 0) >= DegradedAfterFailures,
            string.IsNullOrWhiteSpace(settings.Value.PortalUrl) ? null : settings.Value.PortalUrl);
    }
}

[PlatformOnly]
[RequirePermission(Permissions.PlatformLicensingSync)]
public sealed record RunLicensingSyncCommand : ICommand<SyncOutcome>;

internal sealed class RunLicensingSyncCommandValidator : AbstractValidator<RunLicensingSyncCommand>;

internal sealed class RunLicensingSyncCommandHandler(LicensingSyncService sync, IAuditLogger audit) : ICommandHandler<RunLicensingSyncCommand, SyncOutcome>
{
    public async Task<Result<SyncOutcome>> Handle(RunLicensingSyncCommand request, CancellationToken cancellationToken)
    {
        var outcome = await sync.ReconcileAllAsync(cancellationToken);
        audit.Add("licensing.sync.manual", "LicensingSync", null, $"checked={outcome.TenantsChecked} changed={outcome.TenantsChanged}", outcome.Succeeded);
        return outcome.Succeeded ? outcome : LicensingErrors.Unavailable;
    }
}
