using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Abstractions.Authorization;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Application.Common;
using MonitorCloud.Application.Devices.Contracts;
using MonitorCloud.Application.Licensing.Contracts;
using MonitorCloud.Application.Monitoring.Contracts;
using MonitorCloud.Application.Tenancy.Contracts;
using MonitorCloud.Domain.Audit;
using MonitorCloud.Domain.Identity;
using MonitorCloud.Domain.Tenancy;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Tenancy.Dashboards;

internal static class DashboardMath
{
    public const int DeltaDays = 30;

    public static decimal? Percent(int part, int total) => total == 0 ? null : Math.Round(100m * part / total, 1);

    public static KpiTileDto Tile(string key, int value, int? total = null) => new(key, value, null, null, total is { } t ? Percent(value, t) : null);

    public static KpiTileDto Delta(string key, int value, int current, int previous) =>
        new(key, value, current - previous, previous == 0 ? null : Math.Round(100m * (current - previous) / previous, 1), null);

    public static HealthBreakdownDto Health(DeviceCounts c) => new(c.Devices, c.Healthy, c.Warning, c.Critical, c.Offline);

    public static DeviceCounts Sum(IEnumerable<DeviceCounts> counts) =>
        counts.Aggregate(DeviceCounts.Empty, (a, b) => new DeviceCounts(
            a.Devices + b.Devices, a.Online + b.Online, a.Offline + b.Offline, a.Healthy + b.Healthy, a.Warning + b.Warning, a.Critical + b.Critical, a.Licensed + b.Licensed, a.Unlicensed + b.Unlicensed));

    public const int RecentAlerts = 6;

    /// <summary>Alerts opened per day over the last <paramref name="days"/> days (tenant-local dates).</summary>
    public static async Task<IReadOnlyList<TrendSeriesDto>> IncidentTrendAsync(IAlertDashboardReader alerts, Guid? locationId, int days, string? timeZone, DateTimeOffset now, CancellationToken ct)
    {
        var zone = timeZone is not null && TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var z) ? z : TimeZoneInfo.Utc;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var rows = await alerts.TrendAsync(locationId, today.AddDays(1 - days), today, ct);
        return
        [
            new("Critical", rows.Select(r => new TrendPointDto(r.Day, r.Critical)).ToList()),
            new("Warning", rows.Select(r => new TrendPointDto(r.Day, r.Warning)).ToList()),
            new("Info", rows.Select(r => new TrendPointDto(r.Day, r.Info)).ToList()),
        ];
    }

    public static async Task<IReadOnlyList<RecentAlertDto>> RecentAlertsAsync(
        IAlertDashboardReader alerts, IDeviceNames devices, ILocationLookup locations, ITenantNames? tenants, Guid? locationId, CancellationToken ct)
    {
        var rows = await alerts.RecentAsync(locationId, RecentAlerts, ct);
        var deviceNames = await devices.GetAsync([.. rows.Select(r => r.DeviceId).Distinct()], ct);
        var locationNames = await locations.GetAsync([.. rows.Select(r => r.LocationId).Distinct()], ct);
        var tenantNames = tenants is null ? new Dictionary<Guid, string>() : await tenants.GetAsync([.. rows.Select(r => r.TenantId).Distinct()], ct);
        return rows.Select(r => new RecentAlertDto(r.Id, r.At, r.Severity, r.TenantId, tenantNames.GetValueOrDefault(r.TenantId), r.DeviceId, deviceNames.GetValueOrDefault(r.DeviceId),
            locationNames.GetValueOrDefault(r.LocationId)?.Name, r.Title)).ToList();
    }

    public static IReadOnlyList<NamedCountDto> WithPercent(IEnumerable<(string Name, int Count)> items)
    {
        var list = items.ToList();
        var total = list.Sum(i => i.Count);
        return list.Select(i => new NamedCountDto(i.Name, i.Count, Percent(i.Count, total) ?? 0)).ToList();
    }

    public static async Task<IReadOnlyList<ProblemDeviceDto>> ProblemsAsync(IDeviceDashboardReader devices, ILocationLookup locations, Guid? locationId, CancellationToken ct)
    {
        var rows = await devices.TopProblematicAsync(locationId, 5, ct);
        var names = await locations.GetAsync([.. rows.Select(r => r.LocationId).Distinct()], ct);
        return rows.Select(r => new ProblemDeviceDto(r.Id, r.Name, r.LocationId, names.GetValueOrDefault(r.LocationId)?.Name, r.Issue, r.Health, r.Connection, r.OpenAlerts, r.LastSeenAt)).ToList();
    }
}

// ---------------------------------------------------------------- customer dashboard

[RequirePermission(Permissions.DashboardRead)]
public sealed record GetTenantDashboardQuery(int? TrendDays) : IQuery<TenantDashboardDto>;

internal sealed class GetTenantDashboardQueryValidator : AbstractValidator<GetTenantDashboardQuery>
{
    public GetTenantDashboardQueryValidator() => RuleFor(x => x.TrendDays).Must(d => d is null or 7 or 30).WithMessage("trendDays must be 7 or 30.");
}

/// <summary>Customer Dashboard / Customer Workspace (07 section 5.3). Needs a tenant scope.</summary>
internal sealed class GetTenantDashboardQueryHandler(
    IReadDbContext db, ITenantContext scope, IDeviceStatsDirectory stats, IDeviceDashboardReader devices, ILocationLookup locations, IEntitlementDirectory entitlements,
    IAlertDashboardReader alerts, IDeviceNames deviceNames, TimeProvider clock)
    : IQueryHandler<GetTenantDashboardQuery, TenantDashboardDto>
{
    public async Task<Result<TenantDashboardDto>> Handle(GetTenantDashboardQuery request, CancellationToken cancellationToken)
    {
        if (scope.TenantId is not { } tenantId)
            return CommonErrors.TenantScopeRequired;
        var tenant = await db.Query<Tenant>().SingleOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant is null)
            return TenancyErrors.TenantNotFound;

        var locationRows = await db.Query<Location>()
            .OrderBy(l => l.IsDefault).ThenBy(l => l.Name)
            .Select(l => new { l.Id, l.Name, l.Code, l.City, l.Country, l.IsDefault, Status = l.Status.ToString() })
            .ToListAsync(cancellationToken);
        var perLocation = await stats.ByLocationAsync([.. locationRows.Select(l => l.Id)], cancellationToken);
        var total = DashboardMath.Sum(perLocation.Values);
        var now = clock.GetUtcNow();
        var (current, previous) = await devices.EnrolledAsync(now.AddDays(-2 * DashboardMath.DeltaDays), now.AddDays(-DashboardMath.DeltaDays), cancellationToken);
        var entitlement = (await entitlements.GetAsync([tenantId], cancellationToken)).GetValueOrDefault(tenantId);

        var cards = locationRows.Select(l =>
        {
            var c = perLocation.GetValueOrDefault(l.Id) ?? DeviceCounts.Empty;
            return new LocationCardDto(l.Id, l.Name, l.Code, l.City, l.Country, l.IsDefault, l.Status, c.Devices, c.Online, c.Warning, c.Critical, c.HealthScore);
        }).ToList();
        var byLocation = locationRows
            .Select(l => (l, c: perLocation.GetValueOrDefault(l.Id) ?? DeviceCounts.Empty))
            .Where(x => !x.l.IsDefault || x.c.Devices > 0)
            .Select(x => new LocationStatusRowDto(x.l.Id, x.l.Name, x.c.Devices, x.c.Online, x.c.Healthy, x.c.Warning, x.c.Critical, x.c.HealthScore))
            .ToList();
        var namedLocations = locationRows.Count(l => !l.IsDefault);

        IReadOnlyList<KpiTileDto> tiles =
        [
            DashboardMath.Tile("locations", namedLocations),
            DashboardMath.Delta("devices", total.Devices, current, previous),
            DashboardMath.Tile("online", total.Online, total.Devices),
            DashboardMath.Tile("healthy", total.Healthy, total.Devices),
            DashboardMath.Tile("warning", total.Warning, total.Devices),
            DashboardMath.Tile("critical", total.Critical, total.Devices),
            DashboardMath.Tile("licensed", total.Licensed, total.Devices),
            DashboardMath.Tile("unlicensed", total.Unlicensed, total.Devices),
        ];

        return new TenantDashboardDto(
            new WorkspaceHeaderDto(tenant.Id, tenant.Name, tenant.Status.ToString(), entitlement?.PlanName, namedLocations, total.Devices, tenant.CustomerSince),
            tiles,
            cards.Where(c => !c.IsDefault || c.Devices > 0).ToList(),
            DashboardMath.Health(total),
            await DashboardMath.IncidentTrendAsync(alerts, null, request.TrendDays ?? 7, tenant.TimeZone, now, cancellationToken),
            await DashboardMath.ProblemsAsync(devices, locations, null, cancellationToken),
            byLocation,
            await DashboardMath.RecentAlertsAsync(alerts, deviceNames, locations, null, null, cancellationToken),
            new LicenseSummaryDto(total.Licensed, total.Unlicensed, entitlement?.PlanName, entitlement?.ActiveSeats ?? total.Licensed, entitlement?.MaxDevices, entitlement?.RenewsAt));
    }
}

// ---------------------------------------------------------------- location dashboard

[RequirePermission(Permissions.DashboardRead)]
public sealed record GetLocationDashboardQuery(Guid LocationId, int? TrendDays) : IQuery<LocationDashboardDto>;

internal sealed class GetLocationDashboardQueryValidator : AbstractValidator<GetLocationDashboardQuery>
{
    public GetLocationDashboardQueryValidator()
    {
        RuleFor(x => x.LocationId).NotEmpty();
        RuleFor(x => x.TrendDays).Must(d => d is null or 7 or 30).WithMessage("trendDays must be 7 or 30.");
    }
}

/// <summary>Location Overview (07 section 5.5).</summary>
internal sealed class GetLocationDashboardQueryHandler(
    IReadDbContext db, IDeviceStatsDirectory stats, IDeviceDashboardReader devices, ILocationLookup locations, IEntitlementDirectory entitlements,
    IAlertDashboardReader alerts, IDeviceNames deviceNames, TimeProvider clock)
    : IQueryHandler<GetLocationDashboardQuery, LocationDashboardDto>
{
    public async Task<Result<LocationDashboardDto>> Handle(GetLocationDashboardQuery request, CancellationToken cancellationToken)
    {
        var location = await db.Query<Location>().SingleOrDefaultAsync(l => l.Id == request.LocationId, cancellationToken);
        if (location is null)
            return TenancyErrors.LocationNotFound;
        var tenant = await db.Query<Tenant>().Where(t => t.Id == location.TenantId).Select(t => new { t.Name, t.CustomerSince, t.TimeZone }).SingleAsync(cancellationToken);
        var entitlement = (await entitlements.GetAsync([location.TenantId], cancellationToken)).GetValueOrDefault(location.TenantId);
        var c = (await stats.ByLocationAsync([location.Id], cancellationToken)).GetValueOrDefault(location.Id) ?? DeviceCounts.Empty;
        var resources = await devices.ResourceAveragesAsync(location.Id, cancellationToken);
        var os = await devices.ByOsAsync(location.Id, cancellationToken);

        IReadOnlyList<KpiTileDto> tiles =
        [
            DashboardMath.Tile("devices", c.Devices),
            DashboardMath.Tile("online", c.Online, c.Devices),
            DashboardMath.Tile("healthy", c.Healthy, c.Devices),
            DashboardMath.Tile("warning", c.Warning, c.Devices),
            DashboardMath.Tile("critical", c.Critical, c.Devices),
            DashboardMath.Tile("licensed", c.Licensed, c.Devices),
        ];
        var summary = new LocationSummaryDto(
            location.Id, location.Name, location.Code, location.City, location.Country, location.AddressLine, location.TimeZone, location.ContactName, location.ContactEmail,
            location.ContactPhone, location.IsDefault, tenant.Name, entitlement?.PlanName, tenant.CustomerSince, await devices.LastSeenAsync(location.Id, cancellationToken));

        return new LocationDashboardDto(
            summary,
            tiles,
            await DashboardMath.IncidentTrendAsync(alerts, location.Id, request.TrendDays ?? 7, tenant.TimeZone, clock.GetUtcNow(), cancellationToken),
            DashboardMath.WithPercent(os.Select(o => (o.OsFamily, o.Devices))),
            DashboardMath.Health(c),
            new ResourceAveragesDto(resources.OnlineDevices, resources.Cpu, resources.Ram, resources.Disk, c.HealthScore),
            await DashboardMath.ProblemsAsync(devices, locations, location.Id, cancellationToken),
            await DashboardMath.RecentAlertsAsync(alerts, deviceNames, locations, null, location.Id, cancellationToken));
    }
}

// ---------------------------------------------------------------- platform dashboard

[PlatformOnly]
[RequirePermission(Permissions.PlatformDashboardRead)]
public sealed record GetPlatformDashboardQuery(int? TrendDays, int? SeverityDays) : IQuery<PlatformDashboardDto>;

internal sealed class GetPlatformDashboardQueryValidator : AbstractValidator<GetPlatformDashboardQuery>
{
    public GetPlatformDashboardQueryValidator()
    {
        RuleFor(x => x.TrendDays).Must(d => d is null or 7 or 30).WithMessage("trendDays must be 7 or 30.");
        RuleFor(x => x.SeverityDays).Must(d => d is null or 7 or 30 or 90).WithMessage("severityDays must be 7, 30 or 90.");
    }
}

/// <summary>Platform Admin Dashboard (07 section 5.1).</summary>
internal sealed class GetPlatformDashboardQueryHandler(
    IReadDbContext db, IDeviceStatsDirectory stats, IDeviceDashboardReader devices, IEntitlementStatistics plans, ITenantNames names, IAlertDashboardReader alerts,
    IDeviceNames deviceNames, ILocationLookup locations, TimeProvider clock)
    : IQueryHandler<GetPlatformDashboardQuery, PlatformDashboardDto>
{
    public const int TopCustomers = 5;
    public const int ExpiringRows = 5;
    public const int ActivityRows = 8;

    public async Task<Result<PlatformDashboardDto>> Handle(GetPlatformDashboardQuery request, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var since = now.AddDays(-DashboardMath.DeltaDays);
        var previousSince = now.AddDays(-2 * DashboardMath.DeltaDays);

        var tenants = db.Query<Tenant>().Where(t => t.Status != TenantStatus.Archived);
        var customers = await tenants.CountAsync(cancellationToken);
        var sinceDate = DateOnly.FromDateTime(since.UtcDateTime);
        var previousDate = DateOnly.FromDateTime(previousSince.UtcDateTime);
        var newCustomers = await tenants.CountAsync(t => t.CustomerSince >= sinceDate, cancellationToken);
        var previousCustomers = await tenants.CountAsync(t => t.CustomerSince >= previousDate && t.CustomerSince < sinceDate, cancellationToken);

        var perTenant = await stats.ByTenantAsync(null, cancellationToken);
        var total = DashboardMath.Sum(perTenant.Values);
        var (enrolled, previousEnrolled) = await devices.EnrolledAsync(previousSince, since, cancellationToken);

        IReadOnlyList<KpiTileDto> tiles =
        [
            DashboardMath.Delta("customers", customers, newCustomers, previousCustomers),
            DashboardMath.Delta("devices", total.Devices, enrolled, previousEnrolled),
            DashboardMath.Tile("healthy", total.Healthy, total.Devices),
            DashboardMath.Tile("warning", total.Warning, total.Devices),
            DashboardMath.Tile("critical", total.Critical, total.Devices),
            DashboardMath.Tile("licensed", total.Licensed, total.Devices),
            DashboardMath.Tile("unlicensed", total.Unlicensed, total.Devices),
            DashboardMath.Delta("newDevices", enrolled, enrolled, previousEnrolled),
        ];

        var top = perTenant.OrderByDescending(p => p.Value.Devices).ThenBy(p => p.Key).Take(TopCustomers).ToList();
        var expiring = await plans.ExpiringAsync(ExpiringRows, cancellationToken);
        var tenantNames = await names.GetAsync([.. top.Select(t => t.Key).Concat(expiring.Select(e => e.TenantId)).Distinct()], cancellationToken);

        var activity = await db.Query<AuditRecord>()
            .Where(a => a.Success)
            .OrderByDescending(a => a.At)
            .Take(ActivityRows)
            .Select(a => new { a.At, a.Action, a.ActorName, a.EntityType, a.Details, a.TenantId })
            .ToListAsync(cancellationToken);
        var activityNames = await names.GetAsync([.. activity.Where(a => a.TenantId != null).Select(a => a.TenantId!.Value).Distinct()], cancellationToken);

        var os = await devices.ByOsAsync(null, cancellationToken);
        var distribution = await plans.DistributionAsync(cancellationToken);
        var severity = await alerts.BySeverityAsync(now.AddDays(-(request.SeverityDays ?? 30)), cancellationToken);

        return new PlatformDashboardDto(
            tiles,
            await DashboardMath.IncidentTrendAsync(alerts, null, request.TrendDays ?? 7, null, now, cancellationToken),
            DashboardMath.WithPercent([("Critical", severity.Critical), ("Warning", severity.Warning), ("Info", severity.Info)]),
            severity.Resolved,
            DashboardMath.WithPercent(distribution.Select(d => (d.PlanName, d.Customers))),
            top.Select(t => new TopCustomerDto(t.Key, tenantNames.GetValueOrDefault(t.Key) ?? string.Empty, t.Value.Devices, t.Value.Online, t.Value.HealthScore)).ToList(),
            expiring.Select(e => new ExpiringSubscriptionDto(e.TenantId, tenantNames.GetValueOrDefault(e.TenantId) ?? string.Empty, e.PlanName, e.RenewsAt, e.DaysLeft,
                e.DaysLeft <= 7 ? "Expiring" : e.DaysLeft <= 14 ? "Warning" : "Active")).ToList(),
            await DashboardMath.RecentAlertsAsync(alerts, deviceNames, locations, names, null, cancellationToken),
            DashboardMath.Health(total),
            DashboardMath.WithPercent(os.Select(o => (o.OsFamily, o.Devices))),
            activity.Select(a => new ActivityDto(a.At, a.Action, a.ActorName, a.EntityType, a.Details, a.TenantId is { } id ? activityNames.GetValueOrDefault(id) : null)).ToList());
    }
}
