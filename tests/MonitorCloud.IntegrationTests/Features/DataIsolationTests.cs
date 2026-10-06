using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Domain.Identity;
using MonitorCloud.Domain.Tenancy;
using MonitorCloud.Infrastructure.Persistence;
using MonitorCloud.SharedKernel;
using MonitorCloud.TestShared;

namespace MonitorCloud.IntegrationTests.Features;

/// <summary>The query filters and the write guard of 03 section 4, below the API.</summary>
[Collection(SqlCollection.Name)]
public sealed class DataIsolationTests(SqlServerFixture sql) : FeatureTestBase(sql)
{
    private async Task<T> AsTenantAsync<T>(Guid tenantId, Func<AppDbContext, Task<T>> action, params Guid[] locations)
    {
        await using var scope = App.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantScopeSetter>().RunAsTenant(tenantId, locations);
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    [Fact]
    public async Task Tenant_scope_sees_only_its_own_rows()
    {
        var locations = await AsTenantAsync(World.A.Id, db => db.Set<Location>().Select(l => l.TenantId).Distinct().ToListAsync());
        var users = await AsTenantAsync(World.A.Id, db => db.Set<User>().Select(u => u.TenantId).Distinct().ToListAsync());

        locations.ShouldBe([World.A.Id]);
        users.ShouldBe([World.A.Id]);
    }

    [Fact]
    public async Task Location_scope_limits_locations()
    {
        var ids = await AsTenantAsync(World.A.Id, db => db.Set<Location>().Select(l => l.Id).ToListAsync(), World.A.Location2.Id);

        ids.ShouldBe([World.A.Location2.Id]);
    }

    [Fact]
    public async Task A_closed_scope_sees_nothing()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        (await db.Set<Location>().CountAsync()).ShouldBe(0);
        (await db.Set<User>().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Writing_another_tenants_row_is_rejected()
    {
        var ex = await Should.ThrowAsync<DomainException>(() => AsTenantAsync(World.A.Id, async db =>
        {
            db.Add(Location.Create(World.B.Id, new LocationDetails("Smuggled", "SMUGGLE", null, null, null, "Africa/Cairo", null, null, null), App.Clock.GetUtcNow()));
            return await db.SaveChangesAsync();
        }));

        ex.Error.Code.ShouldBe("AUTH_FORBIDDEN");
        (await App.InDbAsync(db => db.Set<Location>().CountAsync(l => l.Code == "SMUGGLE"))).ShouldBe(0);
    }

    [Fact]
    public async Task Writing_a_platform_row_from_a_tenant_scope_is_rejected()
    {
        await Should.ThrowAsync<DomainException>(() => AsTenantAsync(World.A.Id, async db =>
        {
            db.Add(User.CreatePlatformUser("sneaky@monitor.local", "Sneaky", Roles.PlatformAdmin, "hash", App.Clock.GetUtcNow()));
            return await db.SaveChangesAsync();
        }));
    }

    [Fact]
    public async Task Moving_a_row_to_another_tenant_is_rejected()
    {
        await Should.ThrowAsync<DomainException>(() => AsTenantAsync(World.A.Id, async db =>
        {
            var location = await db.Set<Location>().SingleAsync(l => l.Id == World.A.Location1.Id);
            db.Entry(location).Property(nameof(Location.TenantId)).CurrentValue = World.B.Id;
            return await db.SaveChangesAsync();
        }));
    }
}
