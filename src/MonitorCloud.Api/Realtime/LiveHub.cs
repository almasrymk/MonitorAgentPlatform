using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using MonitorCloud.Application.Devices;

namespace MonitorCloud.Api.Realtime;

/// <summary>The portal's live hub <c>/hubs/live</c> (06 section 5). Every subscription is checked against the caller's scope.</summary>
[Authorize]
public sealed class LiveHub(ISender sender) : Hub
{
    public Task SubscribePlatform() => Join(LiveScope.Platform, null);

    /// <summary>Tenant users join their own tenant; platform users the workspace tenant they pass.</summary>
    public Task SubscribeTenant(Guid? tenantId = null) => Join(LiveScope.Tenant, tenantId);

    public Task SubscribeLocation(Guid locationId) => Join(LiveScope.Location, locationId);

    public Task UnsubscribeLocation(Guid locationId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, LiveGroups.Location(locationId));

    public Task SubscribeDevice(Guid deviceId) => Join(LiveScope.Device, deviceId);

    public Task UnsubscribeDevice(Guid deviceId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, LiveGroups.Device(deviceId));

    private async Task Join(LiveScope scope, Guid? id)
    {
        var group = await sender.Send(new AuthorizeLiveSubscriptionQuery(scope, id), Context.ConnectionAborted);
        if (group.IsFailure)
            throw new HubException(group.Error!.Code);
        await Groups.AddToGroupAsync(Context.ConnectionId, group.Value, Context.ConnectionAborted);
    }
}
