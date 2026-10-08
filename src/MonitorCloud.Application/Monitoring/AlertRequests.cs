using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Abstractions.Audit;
using MonitorCloud.Application.Abstractions.Authorization;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Application.Common;
using MonitorCloud.Application.Devices.Contracts;
using MonitorCloud.Application.Tenancy.Contracts;
using MonitorCloud.Domain.Identity;
using MonitorCloud.Domain.Monitoring;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Monitoring;

internal static class AlertFilters
{
    public static readonly string[] Statuses = ["open", "resolved", "all"];
    public static readonly string[] Sorts = ["lastSeen", "firstSeen", "severity"];

    public static bool IsSeverity(string? value) => value is null || Enum.TryParse<AlertSeverity>(value, true, out _);

    public static IQueryable<Alert> Apply(IQueryable<Alert> query, Guid? locationId, Guid? deviceId, string? severity, string? status, string? category, DateTimeOffset? from, DateTimeOffset? to, (string Key, bool Descending) sort = default)
    {
        if (locationId is { } l)
            query = query.Where(a => a.LocationId == l);
        if (deviceId is { } d)
            query = query.Where(a => a.DeviceId == d);
        if (!string.IsNullOrWhiteSpace(severity))
        {
            var s = Enum.Parse<AlertSeverity>(severity, true);
            query = query.Where(a => a.Severity == s);
        }

        query = status?.ToLowerInvariant() switch
        {
            "resolved" => query.Where(a => a.Status == AlertStatus.Resolved),
            "all" => query,
            _ => query.Where(a => a.Status == AlertStatus.Open),
        };
        if (!string.IsNullOrWhiteSpace(category))
        {
            var c = AlertCategories.Normalize(category);
            query = query.Where(a => a.Category == c);
        }

        if (from is { } f)
            query = query.Where(a => a.LastSeenAt >= f);
        if (to is { } t)
            query = query.Where(a => a.FirstSeenAt <= t);
        // Newest first by default; "-lastSeen" gives the oldest first.
        return (sort.Key, sort.Descending) switch
        {
            ("firstSeen", false) => query.OrderByDescending(a => a.FirstSeenAt).ThenBy(a => a.Id),
            ("firstSeen", true) => query.OrderBy(a => a.FirstSeenAt).ThenBy(a => a.Id),
            ("severity", false) => query.OrderByDescending(a => a.Severity == AlertSeverity.Critical ? 3 : a.Severity == AlertSeverity.Warning ? 2 : 1).ThenByDescending(a => a.LastSeenAt).ThenBy(a => a.Id),
            ("severity", true) => query.OrderBy(a => a.Severity == AlertSeverity.Critical ? 3 : a.Severity == AlertSeverity.Warning ? 2 : 1).ThenByDescending(a => a.LastSeenAt).ThenBy(a => a.Id),
            (_, true) => query.OrderBy(a => a.LastSeenAt).ThenBy(a => a.Id),
            _ => query.OrderByDescending(a => a.LastSeenAt).ThenBy(a => a.Id),
        };
    }

    public static async Task<IReadOnlyList<AlertDto>> ToDtosAsync(IReadOnlyList<Alert> alerts, IDeviceNames devices, ILocationLookup locations, CancellationToken ct)
    {
        var deviceNames = await devices.GetAsync([.. alerts.Select(a => a.DeviceId).Distinct()], ct);
        var locationNames = await locations.GetAsync([.. alerts.Select(a => a.LocationId).Distinct()], ct);
        return alerts.Select(a => ToDto(a, deviceNames.GetValueOrDefault(a.DeviceId), locationNames.GetValueOrDefault(a.LocationId)?.Name)).ToList();
    }

    public static AlertDto ToDto(Alert a, string? deviceName, string? locationName) => new(
        a.Id, a.DeviceId, deviceName, a.LocationId, locationName, a.IssueKey, a.Category, a.Severity.ToString(), a.Title, a.Message, a.Status.ToString(), a.Source.ToString(),
        a.FirstSeenAt, a.LastSeenAt, a.Occurrences, a.AcknowledgedAt, a.ResolvedAt, a.ResolvedBy?.ToString(), a.IsOpen && !CloudIssues.IsSelfResolving(a.IssueKey));
}

[RequirePermission(Permissions.AlertsRead)]
public sealed record GetAlertsQuery(
    Guid? LocationId, Guid? DeviceId, string? Severity, string? Status, string? Category, DateTimeOffset? From, DateTimeOffset? To, string? Sort, int? Page, int? PageSize)
    : IQuery<PagedResult<AlertDto>>;

internal sealed class GetAlertsQueryValidator : AbstractValidator<GetAlertsQuery>
{
    public GetAlertsQueryValidator()
    {
        RuleFor(x => x.Severity).Must(AlertFilters.IsSeverity).WithMessage("severity must be critical, warning or info.");
        RuleFor(x => x.Status).Must(s => s is null || AlertFilters.Statuses.Contains(s.ToLowerInvariant())).WithMessage("status must be open, resolved or all.");
    }
}

internal sealed class GetAlertsQueryHandler(IReadDbContext db, IDeviceNames devices, ILocationLookup locations) : IQueryHandler<GetAlertsQuery, PagedResult<AlertDto>>
{
    public async Task<Result<PagedResult<AlertDto>>> Handle(GetAlertsQuery request, CancellationToken cancellationToken)
    {
        var sort = Paging.ResolveSort(request.Sort, "lastSeen", AlertFilters.Sorts);
        if (sort.IsFailure)
            return sort.Error!;
        var (page, pageSize) = Paging.Normalize(request.Page, request.PageSize);
        var query = AlertFilters.Apply(db.Query<Alert>(), request.LocationId, request.DeviceId, request.Severity, request.Status, request.Category, request.From, request.To, sort.Value);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PagedResult<AlertDto>(await AlertFilters.ToDtosAsync(rows, devices, locations, cancellationToken), total, page, pageSize);
    }
}

[RequirePermission(Permissions.AlertsRead)]
public sealed record GetDeviceAlertsQuery(Guid DeviceId, string? Status, string? Sort, int? Page, int? PageSize) : IQuery<PagedResult<AlertDto>>;

internal sealed class GetDeviceAlertsQueryValidator : AbstractValidator<GetDeviceAlertsQuery>
{
    public GetDeviceAlertsQueryValidator() =>
        RuleFor(x => x.Status).Must(s => s is null || AlertFilters.Statuses.Contains(s.ToLowerInvariant())).WithMessage("status must be open, resolved or all.");
}

internal sealed class GetDeviceAlertsQueryHandler(IReadDbContext db, IDeviceNames devices, ILocationLookup locations, IDeviceDirectory directory)
    : IQueryHandler<GetDeviceAlertsQuery, PagedResult<AlertDto>>
{
    public async Task<Result<PagedResult<AlertDto>>> Handle(GetDeviceAlertsQuery request, CancellationToken cancellationToken)
    {
        if (await directory.FindAsync(request.DeviceId, cancellationToken) is null)
            return IssueRules.DeviceNotFound;
        var sort = Paging.ResolveSort(request.Sort, "lastSeen", AlertFilters.Sorts);
        if (sort.IsFailure)
            return sort.Error!;
        var (page, pageSize) = Paging.Normalize(request.Page, request.PageSize);
        var query = AlertFilters.Apply(db.Query<Alert>(), null, request.DeviceId, null, request.Status, null, null, null, sort.Value);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PagedResult<AlertDto>(await AlertFilters.ToDtosAsync(rows, devices, locations, cancellationToken), total, page, pageSize);
    }
}

[RequirePermission(Permissions.AlertsRead)]
public sealed record GetAlertQuery(Guid Id) : IQuery<AlertDto>;

internal sealed class GetAlertQueryHandler(IReadDbContext db, IDeviceNames devices, ILocationLookup locations) : IQueryHandler<GetAlertQuery, AlertDto>
{
    public async Task<Result<AlertDto>> Handle(GetAlertQuery request, CancellationToken cancellationToken)
    {
        var alert = await db.Query<Alert>().SingleOrDefaultAsync(a => a.Id == request.Id, cancellationToken);
        if (alert is null)
            return MonitoringErrors.AlertNotFound;
        return (await AlertFilters.ToDtosAsync([alert], devices, locations, cancellationToken))[0];
    }
}

[RequirePermission(Permissions.AlertsManage)]
public sealed record AcknowledgeAlertCommand(Guid Id) : ICommand;

internal sealed class AcknowledgeAlertCommandValidator : AbstractValidator<AcknowledgeAlertCommand>
{
    public AcknowledgeAlertCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

internal sealed class AcknowledgeAlertCommandHandler(IAppDbContext db, ICurrentUser user, IAuditLogger audit, TimeProvider clock) : ICommandHandler<AcknowledgeAlertCommand>
{
    public async Task<Result> Handle(AcknowledgeAlertCommand request, CancellationToken cancellationToken)
    {
        var alert = await db.Set<Alert>().SingleOrDefaultAsync(a => a.Id == request.Id, cancellationToken);
        if (alert is null)
            return MonitoringErrors.AlertNotFound;
        if (!alert.IsOpen)
            return MonitoringErrors.AlreadyResolved;
        alert.Acknowledge(user.UserId ?? Guid.Empty, clock.GetUtcNow());
        audit.Add("alert.acknowledged", "Alert", alert.Id.ToString(), alert.Title);
        return Result.Success();
    }
}

[RequirePermission(Permissions.AlertsManage)]
public sealed record ResolveAlertCommand(Guid Id) : ICommand;

internal sealed class ResolveAlertCommandValidator : AbstractValidator<ResolveAlertCommand>
{
    public ResolveAlertCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

internal sealed class ResolveAlertCommandHandler(IAppDbContext db, IAuditLogger audit, TimeProvider clock) : ICommandHandler<ResolveAlertCommand>
{
    public async Task<Result> Handle(ResolveAlertCommand request, CancellationToken cancellationToken)
    {
        var alert = await db.Set<Alert>().SingleOrDefaultAsync(a => a.Id == request.Id, cancellationToken);
        if (alert is null)
            return MonitoringErrors.AlertNotFound;
        if (!alert.IsOpen)
            return MonitoringErrors.AlreadyResolved;
        if (CloudIssues.IsSelfResolving(alert.IssueKey))
            return MonitoringErrors.SelfResolving;
        alert.Resolve(ResolvedBy.User, clock.GetUtcNow());
        audit.Add("alert.resolved", "Alert", alert.Id.ToString(), alert.Title);
        return Result.Success();
    }
}

[PlatformOnly]
[RequirePermission(Permissions.PlatformDashboardRead)]
public sealed record GetPlatformAlertsQuery(string? Severity, Guid? TenantId, string? Status, DateTimeOffset? From, DateTimeOffset? To, string? Sort, int? Page, int? PageSize)
    : IQuery<PagedResult<PlatformAlertDto>>;

internal sealed class GetPlatformAlertsQueryValidator : AbstractValidator<GetPlatformAlertsQuery>
{
    public GetPlatformAlertsQueryValidator()
    {
        RuleFor(x => x.Severity).Must(AlertFilters.IsSeverity).WithMessage("severity must be critical, warning or info.");
        RuleFor(x => x.Status).Must(s => s is null || AlertFilters.Statuses.Contains(s.ToLowerInvariant())).WithMessage("status must be open, resolved or all.");
    }
}

internal sealed class GetPlatformAlertsQueryHandler(IReadDbContext db, IDeviceNames devices, ILocationLookup locations, ITenantNames tenants)
    : IQueryHandler<GetPlatformAlertsQuery, PagedResult<PlatformAlertDto>>
{
    public async Task<Result<PagedResult<PlatformAlertDto>>> Handle(GetPlatformAlertsQuery request, CancellationToken cancellationToken)
    {
        var sort = Paging.ResolveSort(request.Sort, "lastSeen", AlertFilters.Sorts);
        if (sort.IsFailure)
            return sort.Error!;
        var (page, pageSize) = Paging.Normalize(request.Page, request.PageSize);
        var query = db.Query<Alert>();
        if (request.TenantId is { } tenantId)
            query = query.Where(a => a.TenantId == tenantId);
        query = AlertFilters.Apply(query, null, null, request.Severity, request.Status ?? "all", null, request.From, request.To, sort.Value);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var deviceNames = await devices.GetAsync([.. rows.Select(a => a.DeviceId).Distinct()], cancellationToken);
        var locationNames = await locations.GetAsync([.. rows.Select(a => a.LocationId).Distinct()], cancellationToken);
        var tenantNames = await tenants.GetAsync([.. rows.Select(a => a.TenantId).Distinct()], cancellationToken);
        var items = rows.Select(a => new PlatformAlertDto(
            a.Id, a.TenantId, tenantNames.GetValueOrDefault(a.TenantId), a.DeviceId, deviceNames.GetValueOrDefault(a.DeviceId), a.LocationId,
            locationNames.GetValueOrDefault(a.LocationId)?.Name, a.Category, a.Severity.ToString(), a.Title, a.Message, a.Status.ToString(), a.FirstSeenAt, a.LastSeenAt,
            a.Occurrences)).ToList();
        return new PagedResult<PlatformAlertDto>(items, total, page, pageSize);
    }
}

[RequirePermission(Permissions.DevicesRead)]
public sealed record GetDeviceMonitorPointsQuery(Guid DeviceId) : IQuery<IReadOnlyList<MonitorPointDto>>;

internal sealed class GetDeviceMonitorPointsQueryValidator : AbstractValidator<GetDeviceMonitorPointsQuery>
{
    public GetDeviceMonitorPointsQueryValidator() => RuleFor(x => x.DeviceId).NotEmpty();
}

internal sealed class GetDeviceMonitorPointsQueryHandler(IReadDbContext db, IDeviceDirectory directory) : IQueryHandler<GetDeviceMonitorPointsQuery, IReadOnlyList<MonitorPointDto>>
{
    public async Task<Result<IReadOnlyList<MonitorPointDto>>> Handle(GetDeviceMonitorPointsQuery request, CancellationToken cancellationToken)
    {
        if (await directory.FindAsync(request.DeviceId, cancellationToken) is null)
            return IssueRules.DeviceNotFound;
        var points = await db.Query<MonitorPoint>().Where(p => p.DeviceId == request.DeviceId).OrderBy(p => p.SortOrder).ThenBy(p => p.DisplayName).ToListAsync(cancellationToken);
        var ids = points.Select(p => p.Id).ToList();
        var states = await db.Query<MonitorPointState>().Where(s => ids.Contains(s.MonitorPointId)).ToDictionaryAsync(s => s.MonitorPointId, cancellationToken);
        IReadOnlyList<MonitorPointDto> items = points.Select(p => MonitorPointDtos.From(p, states.GetValueOrDefault(p.Id))).ToList();
        return Result.Success(items);
    }
}
