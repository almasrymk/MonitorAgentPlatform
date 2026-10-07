using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Abstractions.Audit;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Application.Tenancy.Contracts;
using MonitorCloud.Domain.Tenancy;

namespace MonitorCloud.Application.Tenancy;

internal sealed class LinkedTenantDirectory(IReadDbContext db) : ILinkedTenantDirectory
{
    public async Task<IReadOnlyList<(Guid CustomerId, Guid TenantId)>> AllLinkedAsync(CancellationToken ct) =>
        (await db.Query<Tenant>()
            .Where(t => t.LicensingCustomerId != null && t.Status != TenantStatus.Archived)
            .Select(t => new { Customer = t.LicensingCustomerId!.Value, t.Id })
            .ToListAsync(ct))
        .Select(x => (x.Customer, x.Id))
        .ToList();

    public async Task<IReadOnlyList<(Guid CustomerId, Guid TenantId)>> FindByLicensingCustomersAsync(IReadOnlyCollection<Guid> customerIds, CancellationToken ct) =>
        (await db.Query<Tenant>()
            .Where(t => t.LicensingCustomerId != null && customerIds.Contains(t.LicensingCustomerId.Value) && t.Status != TenantStatus.Archived)
            .Select(t => new { Customer = t.LicensingCustomerId!.Value, t.Id })
            .ToListAsync(ct))
        .Select(x => (x.Customer, x.Id))
        .ToList();
}

/// <summary>
/// Creates the tenant of a new Licensing customer: name and country from Licensing, code = slug of the name made unique,
/// default location (through <see cref="TenantCreatedV1"/>) and entitlement. No user is created (04 section 4.4).
/// </summary>
internal sealed class TenantProvisioning(IAppDbContext db, IUnitOfWork unitOfWork, IAuditLogger audit, TimeProvider clock) : ITenantProvisioning
{
    public async Task<ProvisionedTenant> EnsureTenantAsync(Guid licensingCustomerId, string name, string? country, CancellationToken ct)
    {
        var existing = await db.Set<Tenant>().SingleOrDefaultAsync(t => t.LicensingCustomerId == licensingCustomerId, ct);
        if (existing is not null)
            return new ProvisionedTenant(existing.Id, false, existing.Status.ToString());

        var baseCode = Tenant.CodeFromName(name);
        var code = baseCode;
        for (var n = 2; await db.Set<Tenant>().AnyAsync(t => t.Code == code, ct); n++)
        {
            var suffix = $"-{n}";
            code = (baseCode.Length + suffix.Length > Tenant.CodeMaxLength ? baseCode[..(Tenant.CodeMaxLength - suffix.Length)] : baseCode) + suffix;
        }

        var now = clock.GetUtcNow();
        var tenant = Tenant.Create(name, code, string.IsNullOrWhiteSpace(country) ? "Unknown" : country, "Unknown", null, DateOnly.FromDateTime(now.UtcDateTime), licensingCustomerId, now);
        db.Set<Tenant>().Add(tenant);
        // The default location is created here as well (not only by CreateDefaultLocation from the outbox): a device
        // enrolling for a brand-new customer must land in "Unassigned" within the same request (04 section 4.1).
        db.Set<Location>().Add(Location.CreateDefault(tenant.Id, tenant.TimeZone, now));
        audit.Add("tenant.provisioned", "Tenant", tenant.Id.ToString(), $"Licensing customer {licensingCustomerId}");
        await unitOfWork.SaveChangesAsync(ct);
        return new ProvisionedTenant(tenant.Id, true, tenant.Status.ToString());
    }
}
