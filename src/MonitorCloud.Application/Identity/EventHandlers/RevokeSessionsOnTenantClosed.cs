using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Domain.Identity;
using MonitorCloud.Domain.Tenancy;

namespace MonitorCloud.Application.Identity.EventHandlers;

/// <summary>A suspended or archived customer's users lose their sessions (02 section 12).</summary>
internal sealed class RevokeSessionsOnTenantClosed(IAppDbContext db, TimeProvider clock)
    : IIntegrationEventHandler<TenantSuspendedV1>, IIntegrationEventHandler<TenantArchivedV1>
{
    public Task HandleAsync(TenantSuspendedV1 integrationEvent, CancellationToken cancellationToken) =>
        RevokeAsync(integrationEvent.TenantId, cancellationToken);

    public Task HandleAsync(TenantArchivedV1 integrationEvent, CancellationToken cancellationToken) =>
        RevokeAsync(integrationEvent.TenantId, cancellationToken);

    private async Task RevokeAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var userIds = db.Set<User>().Where(u => u.TenantId == tenantId).Select(u => u.Id);
        var tokens = await db.Set<RefreshToken>().Where(t => t.RevokedAt == null && userIds.Contains(t.UserId)).ToListAsync(cancellationToken);
        var now = clock.GetUtcNow();
        foreach (var token in tokens)
            token.Revoke(now);
    }
}
