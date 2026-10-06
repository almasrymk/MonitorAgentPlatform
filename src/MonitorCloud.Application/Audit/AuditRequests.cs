using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Abstractions.Authorization;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Application.Common;
using MonitorCloud.Domain.Audit;
using MonitorCloud.Domain.Identity;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Audit;

public sealed record AuditRecordDto(
    Guid Id,
    Guid? TenantId,
    string ActorType,
    Guid? ActorId,
    string ActorName,
    string Action,
    string EntityType,
    string? EntityId,
    bool Success,
    string? Details,
    string? Ip,
    DateTimeOffset At);

[PlatformOnly]
[RequirePermission(Permissions.PlatformAuditRead)]
public sealed record GetPlatformAuditQuery(Guid? TenantId, string? Actor, string? Action, DateTimeOffset? From, DateTimeOffset? To, string? Sort, int? Page, int? PageSize)
    : IQuery<PagedResult<AuditRecordDto>>;

internal sealed class GetPlatformAuditQueryHandler(IReadDbContext db) : IQueryHandler<GetPlatformAuditQuery, PagedResult<AuditRecordDto>>
{
    public Task<Result<PagedResult<AuditRecordDto>>> Handle(GetPlatformAuditQuery request, CancellationToken cancellationToken)
    {
        var query = db.Query<AuditRecord>();
        if (request.TenantId is { } tenantId)
            query = query.Where(a => a.TenantId == tenantId);
        return AuditQueries.PageAsync(query, request.Actor, request.Action, request.From, request.To, request.Sort, request.Page, request.PageSize, cancellationToken);
    }
}

[RequirePermission(Permissions.AuditRead)]
public sealed record GetAuditQuery(string? Actor, string? Action, DateTimeOffset? From, DateTimeOffset? To, string? Sort, int? Page, int? PageSize)
    : IQuery<PagedResult<AuditRecordDto>>;

internal sealed class GetAuditQueryHandler(IReadDbContext db) : IQueryHandler<GetAuditQuery, PagedResult<AuditRecordDto>>
{
    // The query filter limits the records to the scope's tenant.
    public Task<Result<PagedResult<AuditRecordDto>>> Handle(GetAuditQuery request, CancellationToken cancellationToken) =>
        AuditQueries.PageAsync(db.Query<AuditRecord>(), request.Actor, request.Action, request.From, request.To, request.Sort, request.Page, request.PageSize, cancellationToken);
}

internal static class AuditQueries
{
    private static readonly string[] Sorts = ["at"];

    public static async Task<Result<PagedResult<AuditRecordDto>>> PageAsync(
        IQueryable<AuditRecord> query, string? actor, string? action, DateTimeOffset? from, DateTimeOffset? to, string? sortValue, int? pageValue, int? pageSizeValue, CancellationToken cancellationToken)
    {
        var sort = Paging.ResolveSort(sortValue ?? "-at", "at", Sorts);
        if (sort.IsFailure)
            return sort.Error!;
        var (page, pageSize) = Paging.Normalize(pageValue, pageSizeValue);

        if (!string.IsNullOrWhiteSpace(actor))
            query = query.Where(a => a.ActorName.Contains(actor.Trim()));
        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(a => a.Action.StartsWith(action.Trim()));
        if (from is { } f)
            query = query.Where(a => a.At >= f);
        if (to is { } t)
            query = query.Where(a => a.At <= t);
        query = sort.Value.Descending ? query.OrderByDescending(a => a.At).ThenByDescending(a => a.Id) : query.OrderBy(a => a.At).ThenBy(a => a.Id);

        var total = await query.CountAsync(cancellationToken);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new AuditRecordDto(a.Id, a.TenantId, a.ActorType.ToString(), a.ActorId, a.ActorName, a.Action, a.EntityType, a.EntityId, a.Success, a.Details, a.Ip, a.At))
            .ToListAsync(cancellationToken);
        return new PagedResult<AuditRecordDto>(items, total, page, pageSize);
    }
}
