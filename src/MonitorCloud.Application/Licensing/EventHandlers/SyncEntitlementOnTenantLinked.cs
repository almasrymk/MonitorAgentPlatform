using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Licensing.Contracts;
using MonitorCloud.Domain.Tenancy;

namespace MonitorCloud.Application.Licensing.EventHandlers;

/// <summary>A tenant created for a Licensing customer gets its entitlement right away (MC-205).</summary>
internal sealed class SyncEntitlementOnTenantLinked(IEntitlementRefresher refresher) : IIntegrationEventHandler<TenantCreatedV1>
{
    public async Task HandleAsync(TenantCreatedV1 integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.LicensingCustomerId is { } customerId)
            await refresher.RefreshTenantAsync(integrationEvent.TenantId, customerId, cancellationToken);
    }
}
