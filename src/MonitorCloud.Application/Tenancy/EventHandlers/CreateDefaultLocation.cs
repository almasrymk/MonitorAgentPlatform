using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Domain.Tenancy;

namespace MonitorCloud.Application.Tenancy.EventHandlers;

/// <summary>Every tenant gets its default "Unassigned" location (02 section 2).</summary>
internal sealed class CreateDefaultLocation(IAppDbContext db, TimeProvider clock) : IIntegrationEventHandler<TenantCreatedV1>
{
    public async Task HandleAsync(TenantCreatedV1 integrationEvent, CancellationToken cancellationToken)
    {
        if (await db.Set<Location>().AnyAsync(l => l.TenantId == integrationEvent.TenantId && l.IsDefault, cancellationToken))
            return;
        db.Set<Location>().Add(Location.CreateDefault(integrationEvent.TenantId, integrationEvent.TimeZone, clock.GetUtcNow()));
    }
}
