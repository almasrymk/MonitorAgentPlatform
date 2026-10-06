using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MonitorCloud.Infrastructure.Persistence;

namespace MonitorCloud.Infrastructure.Messaging;

/// <summary>Degraded when the oldest pending outbox message is older than the threshold; unhealthy when messages are dead-lettered.</summary>
internal sealed class OutboxLagHealthCheck(AppDbContext db, TimeProvider timeProvider) : IHealthCheck
{
    internal static readonly TimeSpan LagThreshold = TimeSpan.FromMinutes(5);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var oldest = await db.OutboxMessages.AsNoTracking()
            .Where(m => m.ProcessedAt == null && !m.DeadLettered)
            .OrderBy(m => m.OccurredAt)
            .Select(m => (DateTimeOffset?)m.OccurredAt)
            .FirstOrDefaultAsync(cancellationToken);
        var deadLettered = await db.OutboxMessages.AsNoTracking().CountAsync(m => m.DeadLettered, cancellationToken);

        var data = new Dictionary<string, object> { ["deadLettered"] = deadLettered };
        if (oldest is { } occurredAt)
        {
            var lag = timeProvider.GetUtcNow() - occurredAt;
            data["lagSeconds"] = (int)lag.TotalSeconds;
            if (lag > LagThreshold)
                return HealthCheckResult.Degraded("Outbox is lagging.", data: data);
        }

        return HealthCheckResult.Healthy("Outbox is current.", data);
    }
}
