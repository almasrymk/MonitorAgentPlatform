using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MonitorCloud.Domain.Licensing;
using MonitorCloud.Infrastructure.Persistence;

namespace MonitorCloud.Infrastructure.Licensing;

/// <summary>Degraded after 5 consecutive failed syncs (04 section 4.5). Never unhealthy: customers are not locked out.</summary>
internal sealed class LicensingHealthCheck(AppDbContext db) : IHealthCheck
{
    public const int DegradedAfter = 5;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var state = await db.Set<LicensingSyncState>().AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        var failures = state?.ConsecutiveFailures ?? 0;
        var data = new Dictionary<string, object> { ["consecutiveFailures"] = failures };
        if (state?.LastSuccessAt is { } last)
            data["lastSuccessAt"] = last.ToString("O");
        return failures >= DegradedAfter
            ? HealthCheckResult.Degraded("Licensing sync is failing.", data: data)
            : HealthCheckResult.Healthy("Licensing sync is current.", data);
    }
}
