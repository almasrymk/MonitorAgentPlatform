using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Abstractions.Authorization;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Application.Common;
using MonitorCloud.Application.Tenancy.Contracts;
using MonitorCloud.Domain.Devices;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Devices;

public enum LiveScope
{
    Platform,
    Tenant,
    Location,
    Device,
}

/// <summary>Live hub groups (06 section 5).</summary>
public static class LiveGroups
{
    public const string Platform = "platform";

    public static string Tenant(Guid id) => $"tenant:{id}";

    public static string Location(Guid id) => $"location:{id}";

    public static string Device(Guid id) => $"device:{id}";
}

/// <summary>
/// Checks a live-hub subscription against the caller's scope and returns the group to join (06 section 5). Hub
/// invocations do not pass the tenant-context middleware, so the handler checks ownership itself.
/// </summary>
[AllowAuthenticatedUser]
public sealed record AuthorizeLiveSubscriptionQuery(LiveScope Scope, Guid? Id) : IQuery<string>;

internal sealed class AuthorizeLiveSubscriptionQueryValidator : AbstractValidator<AuthorizeLiveSubscriptionQuery>
{
    public AuthorizeLiveSubscriptionQueryValidator() =>
        RuleFor(x => x.Id).NotEmpty().When(x => x.Scope is LiveScope.Location or LiveScope.Device);
}

internal sealed class AuthorizeLiveSubscriptionQueryHandler(IReadDbContext db, ICurrentUser caller, ITenantScopeSetter scope, ILocationLookup locations)
    : IQueryHandler<AuthorizeLiveSubscriptionQuery, string>
{
    public async Task<Result<string>> Handle(AuthorizeLiveSubscriptionQuery request, CancellationToken cancellationToken)
    {
        scope.RunAsSystem();
        var platform = caller.IsPlatform;
        switch (request.Scope)
        {
            case LiveScope.Platform:
                return platform ? LiveGroups.Platform : CommonErrors.Forbidden;

            case LiveScope.Tenant:
            {
                var tenantId = platform ? request.Id : caller.TenantId;
                if (tenantId is null || (!platform && request.Id is { } asked && asked != tenantId))
                    return CommonErrors.Forbidden;
                return LiveGroups.Tenant(tenantId.Value);
            }

            case LiveScope.Location:
            {
                var location = (await locations.GetAsync([request.Id!.Value], cancellationToken)).GetValueOrDefault(request.Id.Value);
                return location is not null && Allowed(location.TenantId, location.Id, platform) ? LiveGroups.Location(location.Id) : CommonErrors.Forbidden;
            }

            default:
            {
                var device = await db.Query<Device>()
                    .Where(d => d.Id == request.Id && d.Status == DeviceStatus.Active)
                    .Select(d => new { d.Id, d.TenantId, d.LocationId })
                    .SingleOrDefaultAsync(cancellationToken);
                return device is not null && Allowed(device.TenantId, device.LocationId, platform) ? LiveGroups.Device(device.Id) : CommonErrors.Forbidden;
            }
        }
    }

    private bool Allowed(Guid tenantId, Guid locationId, bool platform)
    {
        if (platform)
            return true;
        if (caller.TenantId != tenantId)
            return false;
        return caller.LocationScope.Count == 0 || caller.LocationScope.Contains(locationId);
    }
}
