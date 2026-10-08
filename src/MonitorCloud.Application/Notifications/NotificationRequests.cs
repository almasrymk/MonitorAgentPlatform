using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Abstractions.Authorization;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Application.Common;
using MonitorCloud.Application.Devices.Contracts;
using MonitorCloud.Application.Tenancy.Contracts;
using MonitorCloud.Domain.Identity;
using MonitorCloud.Domain.Notifications;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Notifications;

public sealed record NotificationDto(
    Guid Id, Guid? TenantId, string? CustomerName, string Severity, string Category, string Title, string Body, Guid? LocationId, Guid? DeviceId, string? DeviceName,
    Guid? AlertId, DateTimeOffset CreatedAt, bool Read);

public sealed record UnreadCountDto(int Unread);

internal static class NotificationFeed
{
    public static readonly string[] Severities = ["critical", "warning", "info"];
    public static readonly string[] Sorts = ["createdAt"];

    /// <summary>The caller's feed: rows of the scope addressed to everyone or to the caller, restricted to the caller's locations.</summary>
    public static IQueryable<Notification> Visible(IReadDbContext db, ICurrentUser user, ITenantContext scope, bool platformOnly)
    {
        var userId = user.UserId ?? Guid.Empty;
        var query = db.Query<Notification>().Where(n => n.UserId == null || n.UserId == userId);
        if (platformOnly)
            query = query.Where(n => n.TenantId == null);
        var locations = scope.LocationScope.ToList();
        if (locations.Count > 0)
            query = query.Where(n => n.LocationId == null || locations.Contains(n.LocationId.Value));
        return query;
    }

    public static IQueryable<Notification> Filter(IQueryable<Notification> query, Guid? locationId, string? severity, DateTimeOffset? from, DateTimeOffset? to, bool oldestFirst = false)
    {
        if (locationId is { } l)
            query = query.Where(n => n.LocationId == l);
        if (!string.IsNullOrWhiteSpace(severity))
        {
            var s = Severities.First(x => x.Equals(severity, StringComparison.OrdinalIgnoreCase));
            var name = char.ToUpperInvariant(s[0]) + s[1..];
            query = query.Where(n => n.Severity == name);
        }

        if (from is { } f)
            query = query.Where(n => n.CreatedAt >= f);
        if (to is { } t)
            query = query.Where(n => n.CreatedAt <= t);
        return oldestFirst ? query.OrderBy(n => n.CreatedAt).ThenBy(n => n.Id) : query.OrderByDescending(n => n.CreatedAt).ThenBy(n => n.Id);
    }

    public static async Task<PagedResult<NotificationDto>> PageAsync(
        IReadDbContext db, ITenantNames tenants, IDeviceNames devices, IQueryable<Notification> query, Guid userId, int? page, int? pageSize, CancellationToken ct)
    {
        var (p, size) = Paging.Normalize(page, pageSize);
        var total = await query.CountAsync(ct);
        var rows = await query.Skip((p - 1) * size).Take(size).ToListAsync(ct);
        var ids = rows.Select(r => r.Id).ToList();
        var read = await db.Query<NotificationRead>().Where(r => r.UserId == userId && ids.Contains(r.NotificationId)).Select(r => r.NotificationId).ToListAsync(ct);
        var names = await tenants.GetAsync([.. rows.Where(r => r.TenantId != null).Select(r => r.TenantId!.Value).Distinct()], ct);
        var deviceNames = await devices.GetAsync([.. rows.Where(r => r.DeviceId != null).Select(r => r.DeviceId!.Value).Distinct()], ct);
        var items = rows.Select(n => new NotificationDto(
            n.Id, n.TenantId, n.TenantId is { } t ? names.GetValueOrDefault(t) : null, n.Severity, n.Category, n.Title, n.Body, n.LocationId, n.DeviceId,
            n.DeviceId is { } d ? deviceNames.GetValueOrDefault(d) : null, n.AlertId, n.CreatedAt,
            read.Contains(n.Id))).ToList();
        return new PagedResult<NotificationDto>(items, total, p, size);
    }

    public static async Task<int> UnreadAsync(IReadDbContext db, IQueryable<Notification> query, Guid userId, CancellationToken ct) =>
        await query.CountAsync(n => !db.Query<NotificationRead>().Any(r => r.NotificationId == n.Id && r.UserId == userId), ct);

    public static async Task MarkReadAsync(IAppDbContext db, IReadDbContext read, IQueryable<Notification> query, Guid userId, IReadOnlyList<Guid>? ids, bool all, TimeProvider clock, CancellationToken ct)
    {
        if (!all)
        {
            var wanted = ids ?? [];
            query = query.Where(n => wanted.Contains(n.Id));
        }

        var unread = await query.Where(n => !read.Query<NotificationRead>().Any(r => r.NotificationId == n.Id && r.UserId == userId)).Select(n => n.Id).Take(5000).ToListAsync(ct);
        var now = clock.GetUtcNow();
        foreach (var id in unread)
            db.Set<NotificationRead>().Add(NotificationRead.Create(id, userId, now));
    }
}

internal sealed class NotificationListValidator
{
    public static bool IsSeverity(string? value) => value is null || NotificationFeed.Severities.Contains(value.ToLowerInvariant());
}

[RequirePermission(Permissions.NotificationsRead)]
public sealed record GetNotificationsQuery(Guid? LocationId, string? Severity, DateTimeOffset? From, DateTimeOffset? To, string? Sort, int? Page, int? PageSize) : IQuery<PagedResult<NotificationDto>>;

internal sealed class GetNotificationsQueryValidator : AbstractValidator<GetNotificationsQuery>
{
    public GetNotificationsQueryValidator() => RuleFor(x => x.Severity).Must(NotificationListValidator.IsSeverity).WithMessage("severity must be critical, warning or info.");
}

internal sealed class GetNotificationsQueryHandler(IReadDbContext db, ICurrentUser user, ITenantContext scope, ITenantNames tenants, IDeviceNames devices)
    : IQueryHandler<GetNotificationsQuery, PagedResult<NotificationDto>>
{
    public async Task<Result<PagedResult<NotificationDto>>> Handle(GetNotificationsQuery request, CancellationToken cancellationToken)
    {
        var sort = Paging.ResolveSort(request.Sort, "createdAt", NotificationFeed.Sorts);
        if (sort.IsFailure)
            return sort.Error!;
        var query = NotificationFeed.Filter(NotificationFeed.Visible(db, user, scope, false), request.LocationId, request.Severity, request.From, request.To, sort.Value.Descending);
        return await NotificationFeed.PageAsync(db, tenants, devices, query, user.UserId ?? Guid.Empty, request.Page, request.PageSize, cancellationToken);
    }
}

[RequirePermission(Permissions.NotificationsRead)]
public sealed record GetUnreadNotificationCountQuery : IQuery<UnreadCountDto>;

internal sealed class GetUnreadNotificationCountQueryHandler(IReadDbContext db, ICurrentUser user, ITenantContext scope) : IQueryHandler<GetUnreadNotificationCountQuery, UnreadCountDto>
{
    public async Task<Result<UnreadCountDto>> Handle(GetUnreadNotificationCountQuery request, CancellationToken cancellationToken) =>
        new UnreadCountDto(await NotificationFeed.UnreadAsync(db, NotificationFeed.Visible(db, user, scope, false), user.UserId ?? Guid.Empty, cancellationToken));
}

[RequirePermission(Permissions.NotificationsRead)]
public sealed record MarkNotificationsReadCommand(IReadOnlyList<Guid>? Ids, bool All) : ICommand;

internal sealed class MarkNotificationsReadCommandValidator : AbstractValidator<MarkNotificationsReadCommand>
{
    public MarkNotificationsReadCommandValidator() =>
        RuleFor(x => x).Must(x => x.All || x.Ids is { Count: > 0 and <= 500 }).WithMessage("Send ids (at most 500) or all: true.");
}

internal sealed class MarkNotificationsReadCommandHandler(IAppDbContext db, IReadDbContext read, ICurrentUser user, ITenantContext scope, TimeProvider clock)
    : ICommandHandler<MarkNotificationsReadCommand>
{
    public async Task<Result> Handle(MarkNotificationsReadCommand request, CancellationToken cancellationToken)
    {
        await NotificationFeed.MarkReadAsync(db, read, NotificationFeed.Visible(read, user, scope, false), user.UserId ?? Guid.Empty, request.Ids, request.All, clock, cancellationToken);
        return Result.Success();
    }
}

// ---------------------------------------------------------------- platform feed

[PlatformOnly]
[RequirePermission(Permissions.PlatformDashboardRead)]
public sealed record GetPlatformNotificationsQuery(string? Severity, string? Sort, int? Page, int? PageSize) : IQuery<PagedResult<NotificationDto>>;

internal sealed class GetPlatformNotificationsQueryValidator : AbstractValidator<GetPlatformNotificationsQuery>
{
    public GetPlatformNotificationsQueryValidator() => RuleFor(x => x.Severity).Must(NotificationListValidator.IsSeverity).WithMessage("severity must be critical, warning or info.");
}

/// <summary>The platform feed: notifications addressed to the platform (TenantId null).</summary>
internal sealed class GetPlatformNotificationsQueryHandler(IReadDbContext db, ICurrentUser user, ITenantContext scope, ITenantNames tenants, IDeviceNames devices)
    : IQueryHandler<GetPlatformNotificationsQuery, PagedResult<NotificationDto>>
{
    public async Task<Result<PagedResult<NotificationDto>>> Handle(GetPlatformNotificationsQuery request, CancellationToken cancellationToken)
    {
        var sort = Paging.ResolveSort(request.Sort, "createdAt", NotificationFeed.Sorts);
        if (sort.IsFailure)
            return sort.Error!;
        var query = NotificationFeed.Filter(NotificationFeed.Visible(db, user, scope, true), null, request.Severity, null, null, sort.Value.Descending);
        return await NotificationFeed.PageAsync(db, tenants, devices, query, user.UserId ?? Guid.Empty, request.Page, request.PageSize, cancellationToken);
    }
}

[PlatformOnly]
[RequirePermission(Permissions.PlatformDashboardRead)]
public sealed record GetPlatformUnreadCountQuery : IQuery<UnreadCountDto>;

internal sealed class GetPlatformUnreadCountQueryHandler(IReadDbContext db, ICurrentUser user, ITenantContext scope) : IQueryHandler<GetPlatformUnreadCountQuery, UnreadCountDto>
{
    public async Task<Result<UnreadCountDto>> Handle(GetPlatformUnreadCountQuery request, CancellationToken cancellationToken) =>
        new UnreadCountDto(await NotificationFeed.UnreadAsync(db, NotificationFeed.Visible(db, user, scope, true), user.UserId ?? Guid.Empty, cancellationToken));
}

[PlatformOnly]
[RequirePermission(Permissions.PlatformDashboardRead)]
public sealed record MarkPlatformNotificationsReadCommand(IReadOnlyList<Guid>? Ids, bool All) : ICommand;

internal sealed class MarkPlatformNotificationsReadCommandValidator : AbstractValidator<MarkPlatformNotificationsReadCommand>
{
    public MarkPlatformNotificationsReadCommandValidator() =>
        RuleFor(x => x).Must(x => x.All || x.Ids is { Count: > 0 and <= 500 }).WithMessage("Send ids (at most 500) or all: true.");
}

internal sealed class MarkPlatformNotificationsReadCommandHandler(IAppDbContext db, IReadDbContext read, ICurrentUser user, ITenantContext scope, TimeProvider clock)
    : ICommandHandler<MarkPlatformNotificationsReadCommand>
{
    public async Task<Result> Handle(MarkPlatformNotificationsReadCommand request, CancellationToken cancellationToken)
    {
        await NotificationFeed.MarkReadAsync(db, read, NotificationFeed.Visible(read, user, scope, true), user.UserId ?? Guid.Empty, request.Ids, request.All, clock, cancellationToken);
        return Result.Success();
    }
}
