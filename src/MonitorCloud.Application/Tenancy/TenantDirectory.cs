using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Application.Tenancy.Contracts;
using MonitorCloud.Domain.Tenancy;

namespace MonitorCloud.Application.Tenancy;

internal sealed class TenantDirectory(IReadDbContext db) : ITenantDirectory, ILocationDirectory
{
    public async Task<TenantInfo?> FindAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await db.Query<Tenant>()
            .Where(t => t.Id == tenantId)
            .Select(t => new TenantInfo(t.Id, t.Name, t.Code, t.Status.ToString(), t.TimeZone))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyCollection<Guid>> ExistingAsync(Guid tenantId, IReadOnlyCollection<Guid> locationIds, CancellationToken cancellationToken) =>
        await db.Query<Location>()
            .Where(l => l.TenantId == tenantId && locationIds.Contains(l.Id))
            .Select(l => l.Id)
            .ToListAsync(cancellationToken);
}
