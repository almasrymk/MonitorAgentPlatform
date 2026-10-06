using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MonitorCloud.Domain.Identity;
using MonitorCloud.Domain.Tenancy;
using MonitorCloud.Infrastructure.Seeding;
using MonitorCloud.TestShared;

namespace MonitorCloud.IntegrationTests.Features;

[Collection(SqlCollection.Name)]
public sealed class SeedTests(SqlServerFixture sql) : IAsyncLifetime
{
    private readonly TestApp _app = new(sql);

    public async Task InitializeAsync()
    {
        await _app.InitializeAsync();
        await _app.ResetDatabaseAsync();
    }

    public Task DisposeAsync() => _app.DisposeAsync();

    private Task SeedAsync(bool reset = false) => _app.InSystemScopeAsync(sp => sp.GetRequiredService<DemoSeeder>().RunAsync(reset, CancellationToken.None));

    [Fact]
    public async Task Demo_seed_creates_the_customers_users_and_locations_of_08()
    {
        await SeedAsync();

        var tenants = await _app.InDbAsync(db => db.Set<Tenant>().ToListAsync());
        tenants.Count.ShouldBe(48);
        tenants.Count(t => t.Status == TenantStatus.Suspended).ShouldBe(2);
        tenants.Single(t => t.Code == "OASIS").Status.ShouldBe(TenantStatus.Suspended);

        var acme = tenants.Single(t => t.Code == "ACME");
        var acmeLocations = await _app.InDbAsync(db => db.Set<Location>().Where(l => l.TenantId == acme.Id && !l.IsDefault).Select(l => l.Name).ToListAsync());
        acmeLocations.ShouldBe(["Cairo HQ", "Alexandria Branch", "Dubai Office"], ignoreOrder: true);
        var defaults = await _app.InDbAsync(db => db.Set<Location>().CountAsync(l => l.IsDefault));
        defaults.ShouldBe(48);

        var acmeUsers = await _app.InDbAsync(db => db.Set<User>().Where(u => u.TenantId == acme.Id).ToListAsync());
        acmeUsers.Count.ShouldBe(8);
        acmeUsers.Count(u => u.Status == UserStatus.Inactive).ShouldBe(1);
        acmeUsers.Single(u => u.Email == "it.cairo@acme.test").LocationScope.Count.ShouldBe(1);

        var platformUsers = await _app.InDbAsync(db => db.Set<User>().Where(u => u.TenantId == null).Select(u => u.Email).ToListAsync());
        platformUsers.ShouldBe(["admin@monitor.local", "support@monitor.local"], ignoreOrder: true);

        var generatedAdmins = await _app.InDbAsync(db => db.Set<User>().CountAsync(u => u.Email.StartsWith("admin@c")));
        generatedAdmins.ShouldBe(40);
        var emails = await _app.InDbAsync(db => db.Set<User>().Select(u => u.Email).ToListAsync());
        emails.ShouldAllBe(e => e.EndsWith(".test") || e.EndsWith("@monitor.local"));
    }

    [Fact]
    public async Task Running_the_seed_twice_creates_no_duplicates()
    {
        await SeedAsync();
        await SeedAsync();

        (await _app.InDbAsync(db => db.Set<Tenant>().CountAsync())).ShouldBe(48);
        (await _app.InDbAsync(db => db.Set<User>().CountAsync(u => u.TenantId == null))).ShouldBe(2);
    }

    [Fact]
    public async Task Reset_recreates_the_same_data()
    {
        await SeedAsync();
        var before = await _app.InDbAsync(db => db.Set<Tenant>().OrderBy(t => t.Code).Select(t => t.Code + ":" + t.City + ":" + t.Status).ToListAsync());

        await SeedAsync(reset: true);

        var after = await _app.InDbAsync(db => db.Set<Tenant>().OrderBy(t => t.Code).Select(t => t.Code + ":" + t.City + ":" + t.Status).ToListAsync());
        after.ShouldBe(before);
    }

    [Fact]
    public async Task Seeded_users_sign_in_with_the_documented_passwords()
    {
        await SeedAsync();
        using var client = _app.CreateClient();

        (await client.PostJsonAsync("/api/v1/auth/login", new { email = "admin@monitor.local", password = "Admin@12345" })).StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        (await client.PostJsonAsync("/api/v1/auth/login", new { email = "viewer@acme.test", password = "Demo@12345" })).StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        await (await client.PostJsonAsync("/api/v1/auth/login", new { email = "admin@oasis.test", password = "Demo@12345" })).ShouldBeProblemAsync(System.Net.HttpStatusCode.Forbidden, "AUTH_TENANT_SUSPENDED");
    }
}

[Collection(SqlCollection.Name)]
public sealed class BootstrapSeedTests(SqlServerFixture sql) : IAsyncLifetime
{
    private readonly BootstrapApp _app = new(sql);

    public async Task InitializeAsync() => await _app.InitializeAsync();

    public Task DisposeAsync() => _app.DisposeAsync();

    private sealed class BootstrapApp(SqlServerFixture sql) : TestApp(sql)
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Seed:AdminEmail", "first.admin@monitor.local");
            builder.UseSetting("Seed:AdminPassword", "First-Admin#2026");
        }
    }

    [Fact]
    public async Task First_platform_admin_is_created_from_configuration_once()
    {
        var admins = await _app.InDbAsync(db => db.Set<User>().Where(u => u.TenantId == null).ToListAsync());
        admins.ShouldHaveSingleItem().Email.ShouldBe("first.admin@monitor.local");
        admins[0].Role.ShouldBe(Roles.PlatformAdmin);

        await _app.InSystemScopeAsync(sp => sp.GetRequiredService<BootstrapSeeder>().RunAsync(CancellationToken.None));
        (await _app.InDbAsync(db => db.Set<User>().CountAsync(u => u.TenantId == null))).ShouldBe(1);
    }
}
