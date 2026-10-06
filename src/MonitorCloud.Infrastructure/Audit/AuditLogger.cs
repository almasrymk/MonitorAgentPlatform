using Microsoft.Extensions.DependencyInjection;
using MonitorCloud.Application.Abstractions.Audit;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Domain.Audit;
using MonitorCloud.Infrastructure.Persistence;

namespace MonitorCloud.Infrastructure.Audit;

internal sealed class AuditLogger(
    AppDbContext db,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    TimeProvider timeProvider,
    IServiceScopeFactory scopeFactory) : IAuditLogger
{
    public void Add(string action, string entityType, string? entityId, string? details = null, bool success = true) =>
        db.Add(Create(action, entityType, entityId, details, success));

    public async Task WriteNowAsync(string action, string entityType, string? entityId, string? details, bool success, CancellationToken cancellationToken)
    {
        var record = Create(action, entityType, entityId, details, success);
        await using var scope = scopeFactory.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantScopeSetter>().RunAsSystem();
        var isolated = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        isolated.Add(record);
        await isolated.SaveChangesAsync(cancellationToken);
    }

    private AuditRecord Create(string action, string entityType, string? entityId, string? details, bool success)
    {
        var actorType = currentUser.ActorType switch
        {
            ActorType.Device => AuditActorType.Device,
            ActorType.System => AuditActorType.System,
            _ => AuditActorType.User,
        };
        var actorId = currentUser.UserId ?? currentUser.DeviceId;
        return AuditRecord.Create(
            tenantContext.TenantId ?? currentUser.TenantId,
            actorType,
            actorId,
            currentUser.Name,
            action,
            entityType,
            entityId,
            success,
            details,
            currentUser.IpAddress,
            currentUser.CorrelationId,
            timeProvider.GetUtcNow());
    }
}
