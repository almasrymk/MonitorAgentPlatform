using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Abstractions.Audit;
using MonitorCloud.Application.Abstractions.Authorization;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Application.Common;
using MonitorCloud.Application.Devices.Contracts;
using MonitorCloud.Application.Licensing.Contracts;
using MonitorCloud.Application.Tenancy.Contracts;
using MonitorCloud.Domain.Devices;
using MonitorCloud.Domain.Identity;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Devices;

// ---------------------------------------------------------------- queries

[RequirePermission(Permissions.DevicesRead)]
public sealed record GetDevicesQuery(Guid? LocationId, string? Search, string? Os, string? Status, string? License, string? Sort, int? Page, int? PageSize)
    : IQuery<PagedResult<DeviceListItemDto>>;

internal sealed class GetDevicesQueryHandler(IReadDbContext db, ILocationLookup locations) : IQueryHandler<GetDevicesQuery, PagedResult<DeviceListItemDto>>
{
    public static readonly string[] Sorts = ["severity", "name", "lastSeen", "cpu"];

    public async Task<Result<PagedResult<DeviceListItemDto>>> Handle(GetDevicesQuery request, CancellationToken cancellationToken)
    {
        var sort = Paging.ResolveSort(request.Sort, "severity", Sorts);
        if (sort.IsFailure)
            return sort.Error!;
        var (page, pageSize) = Paging.Normalize(request.Page, request.PageSize);

        var query = DeviceQueries.Active(db);
        if (request.LocationId is { } locationId)
            query = query.Where(x => x.State.LocationId == locationId);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(x => x.Device.Name.Contains(term) || x.Device.Hostname.Contains(term) || (x.Device.LocalIp != null && x.Device.LocalIp.Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(request.Os))
        {
            var os = Device.ParseOsFamily(request.Os);
            query = query.Where(x => x.State.OsFamily == os);
        }

        query = request.Status?.ToLowerInvariant() switch
        {
            null or "" => query,
            "online" => query.Where(x => x.State.Connection == ConnectionState.Online),
            "offline" => query.Where(x => x.State.Connection == ConnectionState.Offline),
            "healthy" => query.Where(x => x.State.Health == DeviceHealth.Healthy),
            "warning" => query.Where(x => x.State.Health == DeviceHealth.Warning),
            "critical" => query.Where(x => x.State.Health == DeviceHealth.Critical),
            _ => query.Where(_ => false),
        };
        query = request.License?.ToLowerInvariant() switch
        {
            "licensed" => query.Where(x => x.State.LicenseState == LicenseStateValue.Licensed),
            "unlicensed" => query.Where(x => x.State.LicenseState == LicenseStateValue.Unlicensed),
            _ => query,
        };

        query = (sort.Value.Key, sort.Value.Descending) switch
        {
            ("name", false) => query.OrderBy(x => x.Device.Name).ThenBy(x => x.Device.Id),
            ("name", true) => query.OrderByDescending(x => x.Device.Name).ThenBy(x => x.Device.Id),
            ("lastSeen", false) => query.OrderByDescending(x => x.State.LastSeenAt).ThenBy(x => x.Device.Name),
            ("lastSeen", true) => query.OrderBy(x => x.State.LastSeenAt).ThenBy(x => x.Device.Name),
            ("cpu", false) => query.OrderByDescending(x => x.State.CpuPercent).ThenBy(x => x.Device.Name),
            ("cpu", true) => query.OrderBy(x => x.State.CpuPercent).ThenBy(x => x.Device.Name),
            // Severity: Critical, Warning, offline (Unknown), Healthy (07 section 1, correction 5).
            (_, false) => query.OrderBy(x => x.State.Health == DeviceHealth.Critical ? 0 : x.State.Health == DeviceHealth.Warning ? 1 : x.State.Health == DeviceHealth.Unknown ? 2 : 3)
                .ThenBy(x => x.Device.Name).ThenBy(x => x.Device.Id),
            (_, true) => query.OrderBy(x => x.State.Health == DeviceHealth.Healthy ? 0 : x.State.Health == DeviceHealth.Unknown ? 1 : x.State.Health == DeviceHealth.Warning ? 2 : 3)
                .ThenBy(x => x.Device.Name).ThenBy(x => x.Device.Id),
        };

        var total = await query.CountAsync(cancellationToken);
        var rows = await query.Skip((page - 1) * pageSize).Take(pageSize).Select(x => new
        {
            x.Device.Id, x.Device.Name, x.State.OsFamily, x.Device.OsName, x.Device.LocalIp, x.State.Connection, x.State.Health, x.State.LicenseState,
            x.State.CpuPercent, x.State.RamPercent, x.State.DiskPercent, x.State.LastSeenAt, x.State.UptimeSeconds, x.State.OpenCritical, x.State.OpenWarning, x.State.LocationId,
        }).ToListAsync(cancellationToken);

        var names = await locations.GetAsync([.. rows.Select(r => r.LocationId).Distinct()], cancellationToken);
        var items = rows.Select(r => new DeviceListItemDto(
            r.Id, r.Name, r.OsFamily.ToString(), r.OsName, r.LocalIp, r.Connection.ToString(), r.Health.ToString(), r.LicenseState.ToString(),
            r.CpuPercent, r.RamPercent, r.DiskPercent, DeviceQueries.Utc(r.LastSeenAt), r.UptimeSeconds, r.OpenCritical + r.OpenWarning,
            r.OpenCritical > 0 ? "Critical" : r.OpenWarning > 0 ? "Warning" : "None", r.LocationId, names.GetValueOrDefault(r.LocationId)?.Name)).ToList();
        return new PagedResult<DeviceListItemDto>(items, total, page, pageSize);
    }
}

internal static class DeviceQueries
{
    /// <summary>Member-initialised (not a positional record) so EF Core can translate member access after the join.</summary>
    public sealed class Row
    {
        public required Device Device { get; init; }
        public required DeviceState State { get; init; }
    }

    /// <summary>Active devices with their state; retired devices keep their history but leave every list.</summary>
    public static IQueryable<Row> Active(IReadDbContext db) =>
        from d in db.Query<Device>()
        join s in db.Query<DeviceState>() on d.Id equals s.DeviceId
        where d.Status == DeviceStatus.Active
        select new Row { Device = d, State = s };

    public static DateTimeOffset? Utc(DateTime? value) => value is { } v ? new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)) : null;
}

[RequirePermission(Permissions.DevicesRead)]
public sealed record GetDevicesSummaryQuery(Guid? LocationId) : IQuery<DevicesSummaryDto>;

internal sealed class GetDevicesSummaryQueryHandler(IReadDbContext db, TimeProvider clock) : IQueryHandler<GetDevicesSummaryQuery, DevicesSummaryDto>
{
    public async Task<Result<DevicesSummaryDto>> Handle(GetDevicesSummaryQuery request, CancellationToken cancellationToken)
    {
        var query = DeviceQueries.Active(db);
        if (request.LocationId is { } locationId)
            query = query.Where(x => x.State.LocationId == locationId);
        var since = clock.GetUtcNow().AddDays(-30);
        var counts = await query
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Online = g.Count(x => x.State.Connection == ConnectionState.Online),
                Licensed = g.Count(x => x.State.LicenseState == LicenseStateValue.Licensed),
                Healthy = g.Count(x => x.State.Health == DeviceHealth.Healthy),
                Warning = g.Count(x => x.State.Health == DeviceHealth.Warning),
                Critical = g.Count(x => x.State.Health == DeviceHealth.Critical),
                New = g.Count(x => x.Device.EnrolledAt >= since),
            })
            .SingleOrDefaultAsync(cancellationToken);
        return counts is null
            ? new DevicesSummaryDto(0, 0, 0, 0, 0, 0, 0, 0, 0)
            : new DevicesSummaryDto(counts.Total, counts.Online, counts.Total - counts.Online, counts.Licensed, counts.Total - counts.Licensed, counts.Healthy, counts.Warning, counts.Critical, counts.New);
    }
}

[RequirePermission(Permissions.DevicesRead)]
public sealed record GetDeviceQuery(Guid Id) : IQuery<DeviceDto>;

internal sealed class GetDeviceQueryHandler(IReadDbContext db, ITenantNames tenants, ILocationLookup locations, IDeviceSeats seats) : IQueryHandler<GetDeviceQuery, DeviceDto>
{
    public async Task<Result<DeviceDto>> Handle(GetDeviceQuery request, CancellationToken cancellationToken)
    {
        var row = await DeviceQueries.Active(db).SingleOrDefaultAsync(x => x.Device.Id == request.Id, cancellationToken);
        if (row is null)
            return DeviceErrors.NotFound;
        var (d, s) = (row.Device, row.State);
        var customer = (await tenants.GetAsync([d.TenantId], cancellationToken)).GetValueOrDefault(d.TenantId) ?? string.Empty;
        var location = (await locations.GetAsync([s.LocationId], cancellationToken)).GetValueOrDefault(s.LocationId);
        var seat = await seats.FindAsync(d.Id, cancellationToken);
        return new DeviceDto(d.Id, d.Name, d.Hostname, d.Fingerprint, d.TenantId, customer, s.LocationId, location?.Name ?? string.Empty, s.OsFamily.ToString(),
            d.OsName, d.OsVersion, d.Architecture, d.LocalIp, d.PublicIp, d.MacAddress, d.AgentVersion, d.ProtocolVersion, d.Status.ToString(), d.EnrolledAt,
            s.Connection.ToString(), s.Health.ToString(), s.LicenseState.ToString(), seat?.ReasonCode, DeviceQueries.Utc(s.LastSeenAt), s.UptimeSeconds,
            s.AppliedConfigVersion, null, Versioning.Encode(d.RowVersion));
    }
}

// ---------------------------------------------------------------- commands

[RequirePermission(Permissions.DevicesManage)]
public sealed record UpdateDeviceCommand(Guid Id, string Name, Guid LocationId, string? Version) : ICommand<DeviceListItemDto>;

internal sealed class UpdateDeviceCommandValidator : AbstractValidator<UpdateDeviceCommand>
{
    public UpdateDeviceCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Device.NameMaxLength);
        RuleFor(x => x.LocationId).NotEmpty();
    }
}

internal sealed class UpdateDeviceCommandHandler(IAppDbContext db, IUnitOfWork unitOfWork, ILocationLookup locations, IAuditLogger audit, TimeProvider clock)
    : ICommandHandler<UpdateDeviceCommand, DeviceListItemDto>
{
    public async Task<Result<DeviceListItemDto>> Handle(UpdateDeviceCommand request, CancellationToken cancellationToken)
    {
        var device = await db.Set<Device>().SingleOrDefaultAsync(d => d.Id == request.Id && d.Status == DeviceStatus.Active, cancellationToken);
        if (device is null)
            return DeviceErrors.NotFound;
        db.ExpectVersion(device, Versioning.Decode(request.Version));

        var target = (await locations.GetAsync([request.LocationId], cancellationToken)).GetValueOrDefault(request.LocationId);
        if (target is null || target.TenantId != device.TenantId)
            return DeviceErrors.LocationNotFound;

        var now = clock.GetUtcNow();
        var moved = device.LocationId != target.Id;
        device.Rename(request.Name);
        device.MoveTo(target.Id, now);
        var state = await db.Set<DeviceState>().SingleAsync(s => s.DeviceId == device.Id, cancellationToken);
        state.MoveTo(target.Id, now);
        audit.Add(moved ? "device.moved" : "device.renamed", "Device", device.Id.ToString(), moved ? $"to {target.Name}" : device.Name);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new DeviceListItemDto(device.Id, device.Name, state.OsFamily.ToString(), device.OsName, device.LocalIp, state.Connection.ToString(), state.Health.ToString(),
            state.LicenseState.ToString(), state.CpuPercent, state.RamPercent, state.DiskPercent, DeviceQueries.Utc(state.LastSeenAt), state.UptimeSeconds,
            state.OpenCritical + state.OpenWarning, state.OpenCritical > 0 ? "Critical" : state.OpenWarning > 0 ? "Warning" : "None", target.Id, target.Name);
    }
}

[RequirePermission(Permissions.DevicesManage)]
public sealed record RetireDeviceCommand(Guid Id) : ICommand;

internal sealed class RetireDeviceCommandValidator : AbstractValidator<RetireDeviceCommand>
{
    public RetireDeviceCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

/// <summary>Retires a device: its credential is revoked now and its seat released through <see cref="DeviceRetiredV1"/>.</summary>
internal sealed class RetireDeviceCommandHandler(IAppDbContext db, IAuditLogger audit, ILicensingPolicy policy, TimeProvider clock) : ICommandHandler<RetireDeviceCommand>
{
    public async Task<Result> Handle(RetireDeviceCommand request, CancellationToken cancellationToken)
    {
        var device = await db.Set<Device>().SingleOrDefaultAsync(d => d.Id == request.Id && d.Status == DeviceStatus.Active, cancellationToken);
        if (device is null)
            return DeviceErrors.NotFound;
        var now = clock.GetUtcNow();
        device.Retire(now);
        var credential = await db.Set<DeviceCredential>().SingleOrDefaultAsync(c => c.DeviceId == device.Id, cancellationToken);
        credential?.Revoke(now);
        var state = await db.Set<DeviceState>().SingleOrDefaultAsync(s => s.DeviceId == device.Id, cancellationToken);
        state?.SetConnection(ConnectionState.Offline, now, policy.UnlicensedGrace);
        audit.Add("device.retired", "Device", device.Id.ToString(), device.Name);
        return Result.Success();
    }
}

[RequirePermission(Permissions.DevicesManage)]
public sealed record UnlicenseDeviceCommand(Guid Id) : ICommand;

internal sealed class UnlicenseDeviceCommandValidator : AbstractValidator<UnlicenseDeviceCommand>
{
    public UnlicenseDeviceCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

/// <summary>Releases the device's seat but keeps the device (Unlicensed, D19); the grace period starts now.</summary>
internal sealed class UnlicenseDeviceCommandHandler(IAppDbContext db, IAuditLogger audit, ILicensingPolicy policy, TimeProvider clock) : ICommandHandler<UnlicenseDeviceCommand>
{
    public async Task<Result> Handle(UnlicenseDeviceCommand request, CancellationToken cancellationToken)
    {
        var device = await db.Set<Device>().SingleOrDefaultAsync(d => d.Id == request.Id && d.Status == DeviceStatus.Active, cancellationToken);
        if (device is null)
            return DeviceErrors.NotFound;
        var state = await db.Set<DeviceState>().SingleAsync(s => s.DeviceId == device.Id, cancellationToken);
        if (state.LicenseState == LicenseStateValue.Unlicensed)
            return Result.Success();
        var now = clock.GetUtcNow();
        state.SetLicense(LicenseStateValue.Unlicensed, now, policy.UnlicensedGrace);
        device.RequestUnlicense(now);
        audit.Add("device.unlicensed", "Device", device.Id.ToString(), device.Name);
        return Result.Success();
    }
}
