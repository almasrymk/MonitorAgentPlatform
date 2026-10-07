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
using MonitorCloud.Domain.Tenancy;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Tenancy;

[RequirePermission(Permissions.LocationsRead)]
public sealed record GetLocationsQuery(string? Search, string? Sort, int? Page, int? PageSize) : IQuery<PagedResult<LocationCardDto>>;

internal sealed class GetLocationsQueryHandler(IReadDbContext db, IDeviceStatsDirectory devices) : IQueryHandler<GetLocationsQuery, PagedResult<LocationCardDto>>
{
    private static readonly string[] Sorts = ["name", "code", "city"];

    public async Task<Result<PagedResult<LocationCardDto>>> Handle(GetLocationsQuery request, CancellationToken cancellationToken)
    {
        var sort = Paging.ResolveSort(request.Sort, "name", Sorts);
        if (sort.IsFailure)
            return sort.Error!;
        var (page, pageSize) = Paging.Normalize(request.Page, request.PageSize);

        var query = db.Query<Location>();
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(l => l.Name.Contains(term) || l.Code.Contains(term) || (l.City != null && l.City.Contains(term)));
        }

        // The default "Unassigned" location is listed last.
        query = (sort.Value.Key, sort.Value.Descending) switch
        {
            ("code", false) => query.OrderBy(l => l.IsDefault).ThenBy(l => l.Code),
            ("code", true) => query.OrderBy(l => l.IsDefault).ThenByDescending(l => l.Code),
            ("city", false) => query.OrderBy(l => l.IsDefault).ThenBy(l => l.City).ThenBy(l => l.Name),
            ("city", true) => query.OrderBy(l => l.IsDefault).ThenByDescending(l => l.City).ThenBy(l => l.Name),
            (_, true) => query.OrderBy(l => l.IsDefault).ThenByDescending(l => l.Name).ThenBy(l => l.Id),
            _ => query.OrderBy(l => l.IsDefault).ThenBy(l => l.Name).ThenBy(l => l.Id),
        };

        var total = await query.CountAsync(cancellationToken);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(l => new LocationCardDto(l.Id, l.Name, l.Code, l.City, l.Country, l.IsDefault, l.Status.ToString(), 0, 0, 0, 0, null))
            .ToListAsync(cancellationToken);
        var counts = await devices.ByLocationAsync([.. items.Select(i => i.Id)], cancellationToken);
        items = [.. items.Select(i => counts.TryGetValue(i.Id, out var c)
            ? i with { Devices = c.Devices, Online = c.Online, Warning = c.Warning, Critical = c.Critical, HealthScore = c.HealthScore }
            : i)];
        return new PagedResult<LocationCardDto>(items, total, page, pageSize);
    }
}

[RequirePermission(Permissions.LocationsRead)]
public sealed record GetLocationQuery(Guid Id) : IQuery<LocationDto>;

internal sealed class GetLocationQueryHandler(IReadDbContext db) : IQueryHandler<GetLocationQuery, LocationDto>
{
    public async Task<Result<LocationDto>> Handle(GetLocationQuery request, CancellationToken cancellationToken)
    {
        var location = await db.Query<Location>().SingleOrDefaultAsync(l => l.Id == request.Id, cancellationToken);
        return location is null ? TenancyErrors.LocationNotFound : LocationMapping.ToDto(location);
    }
}

internal static class LocationMapping
{
    public static LocationDto ToDto(Location l) =>
        new(l.Id, l.Name, l.Code, l.City, l.Country, l.AddressLine, l.TimeZone, l.ContactName, l.ContactEmail, l.ContactPhone,
            l.IsDefault, l.Status.ToString(), l.CreatedAt, Versioning.Encode(l.RowVersion));
}

public interface ILocationFields
{
    string Name { get; }
    string Code { get; }
    string? City { get; }
    string? Country { get; }
    string? AddressLine { get; }
    string TimeZone { get; }
    string? ContactName { get; }
    string? ContactEmail { get; }
    string? ContactPhone { get; }
}

internal static class LocationRules
{
    public static void Apply<T>(AbstractValidator<T> validator)
        where T : ILocationFields
    {
        validator.RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        validator.RuleFor(x => x.Code).NotEmpty().MaximumLength(32).Matches("^[A-Za-z0-9_-]+$");
        validator.RuleFor(x => x.City).MaximumLength(100);
        validator.RuleFor(x => x.Country).MaximumLength(100);
        validator.RuleFor(x => x.AddressLine).MaximumLength(300);
        validator.RuleFor(x => x.TimeZone).NotEmpty().Must(TimeZones.IsValid).WithMessage("Unknown time zone.");
        validator.RuleFor(x => x.ContactName).MaximumLength(200);
        validator.RuleFor(x => x.ContactEmail).EmailAddress().MaximumLength(256).When(x => !string.IsNullOrEmpty(x.ContactEmail));
        validator.RuleFor(x => x.ContactPhone).MaximumLength(50);
    }

    public static LocationDetails Details(ILocationFields f) =>
        new(f.Name, f.Code, f.City, f.Country, f.AddressLine, f.TimeZone, f.ContactName, f.ContactEmail, f.ContactPhone);
}

[RequirePermission(Permissions.LocationsManage)]
public sealed record CreateLocationCommand(
    string Name, string Code, string? City, string? Country, string? AddressLine, string TimeZone,
    string? ContactName, string? ContactEmail, string? ContactPhone) : ICommand<LocationDto>, ILocationFields;

internal sealed class CreateLocationCommandValidator : AbstractValidator<CreateLocationCommand>
{
    public CreateLocationCommandValidator() => LocationRules.Apply(this);
}

internal sealed class CreateLocationCommandHandler(IUnitOfWork unitOfWork, IAppDbContext db, ITenantContext scope, ILocationCodeLookup codes, IAuditLogger audit, TimeProvider clock)
    : ICommandHandler<CreateLocationCommand, LocationDto>
{
    public async Task<Result<LocationDto>> Handle(CreateLocationCommand request, CancellationToken cancellationToken)
    {
        var location = Location.Create(scope.TenantId!.Value, LocationRules.Details(request), clock.GetUtcNow());
        // Codes are unique per tenant, including locations outside the caller's location scope.
        if (await codes.IsTakenAsync(location.Code, null, cancellationToken))
            return TenancyErrors.LocationCodeTaken;
        db.Set<Location>().Add(location);
        audit.Add("location.created", "Location", location.Id.ToString(), location.Name);
        // Saved here (the unit of work then has nothing left) so the returned version is the new row version.
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return LocationMapping.ToDto(location);
    }
}

[RequirePermission(Permissions.LocationsManage)]
public sealed record UpdateLocationCommand(
    Guid Id, string Name, string Code, string? City, string? Country, string? AddressLine, string TimeZone,
    string? ContactName, string? ContactEmail, string? ContactPhone, string? Version) : ICommand<LocationDto>, ILocationFields;

internal sealed class UpdateLocationCommandValidator : AbstractValidator<UpdateLocationCommand>
{
    public UpdateLocationCommandValidator() => LocationRules.Apply(this);
}

internal sealed class UpdateLocationCommandHandler(IUnitOfWork unitOfWork, IAppDbContext db, ILocationCodeLookup codes, IAuditLogger audit) : ICommandHandler<UpdateLocationCommand, LocationDto>
{
    public async Task<Result<LocationDto>> Handle(UpdateLocationCommand request, CancellationToken cancellationToken)
    {
        var location = await db.Set<Location>().SingleOrDefaultAsync(l => l.Id == request.Id, cancellationToken);
        if (location is null)
            return TenancyErrors.LocationNotFound;
        db.ExpectVersion(location, Versioning.Decode(request.Version));

        location.Update(LocationRules.Details(request));
        if (await codes.IsTakenAsync(location.Code, location.Id, cancellationToken))
            return TenancyErrors.LocationCodeTaken;
        audit.Add("location.updated", "Location", location.Id.ToString(), location.Name);
        // Saved here (the unit of work then has nothing left) so the returned version is the new row version.
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return LocationMapping.ToDto(location);
    }
}

[RequirePermission(Permissions.LocationsManage)]
public sealed record DeleteLocationCommand(Guid Id) : ICommand;

internal sealed class DeleteLocationCommandValidator : AbstractValidator<DeleteLocationCommand>
{
    public DeleteLocationCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

internal sealed class DeleteLocationCommandHandler(IAppDbContext db, ILocationDeviceCounter devices, IAuditLogger audit) : ICommandHandler<DeleteLocationCommand>
{
    public async Task<Result> Handle(DeleteLocationCommand request, CancellationToken cancellationToken)
    {
        var location = await db.Set<Location>().SingleOrDefaultAsync(l => l.Id == request.Id, cancellationToken);
        if (location is null)
            return TenancyErrors.LocationNotFound;
        location.EnsureCanBeDeleted();
        if (await devices.CountAsync(location.Id, cancellationToken) > 0)
            return TenancyErrors.LocationNotEmpty;
        db.Set<Location>().Remove(location);
        audit.Add("location.deleted", "Location", location.Id.ToString(), location.Name);
        return Result.Success();
    }
}
