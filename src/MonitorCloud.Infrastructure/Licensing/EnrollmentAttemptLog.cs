using Microsoft.Extensions.DependencyInjection;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Application.Licensing.Contracts;
using MonitorCloud.Domain.Licensing;
using MonitorCloud.Infrastructure.Persistence;

namespace MonitorCloud.Infrastructure.Licensing;

/// <summary>Successful attempts join the enrollment transaction; failed ones are written in their own scope.</summary>
internal sealed class EnrollmentAttemptLog(AppDbContext db, IServiceScopeFactory scopes) : IEnrollmentAttemptLog
{
    public void AddSuccess(Guid tenantId, string productKey, string fingerprint, string hostname, string? ip, DateTimeOffset now) =>
        db.Set<EnrollmentAttempt>().Add(EnrollmentAttempt.Record(tenantId, productKey, fingerprint, hostname, ip, null, now));

    public async Task WriteFailureAsync(Guid? tenantId, string? productKey, string? fingerprint, string? hostname, string? ip, string errorCode, DateTimeOffset now, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantScopeSetter>().RunAsSystem();
        var own = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        own.Set<EnrollmentAttempt>().Add(EnrollmentAttempt.Record(tenantId, productKey, fingerprint, hostname, ip, errorCode, now));
        await own.SaveChangesAsync(ct);
    }
}
