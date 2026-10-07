using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MonitorCloud.Application.Common;
using MonitorCloud.Application.Devices;
using MonitorCloud.Application.Tenancy;
using MonitorCloud.Application.Tenancy.Dashboards;
using MonitorCloud.Domain.Identity;
using MonitorCloud.Domain.Tenancy;
using MonitorCloud.Infrastructure.Seeding;
using MonitorCloud.TestShared;

namespace MonitorCloud.IntegrationTests.Features;

/// <summary>The demo seed (08), loaded once for the class into its own database.</summary>
public sealed class SeededDemoFixture : IAsyncLifetime
{
    private readonly SqlServerFixture _sql = new();

    public TestApp App { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _sql.InitializeAsync();
        App = new TestApp(_sql);
        await App.InitializeAsync();
        await App.InSystemScopeAsync(sp => sp.GetRequiredService<DemoSeeder>().RunAsync(reset: false, CancellationToken.None));
    }

    public async Task DisposeAsync()
    {
        await App.DisposeAsync();
        await _sql.DisposeAsync();
    }
}

/// <summary>
/// MC-306 acceptance: every number of the dashboards and cards equals an independent SQL count over the seed
/// (raw SQL against the tables, not the application's queries).
/// </summary>
[Collection(SqlCollection.Name)]
public sealed class SeedDashboardTests(SeededDemoFixture fixture) : IClassFixture<SeededDemoFixture>
{
    private TestApp App => fixture.App;

    private Task<int> CountAsync(string sql) => App.InDbAsync(db => db.Database.SqlQueryRaw<int>(sql).SingleAsync());

    private async Task<HttpClient> ClientAsync(string email, Guid? workspace = null)
    {
        var user = await App.InDbAsync(db => db.Set<User>().SingleAsync(u => u.Email == email));
        return App.ClientFor(user, workspace);
    }

    private Task<Guid> TenantIdAsync(string code) => App.InDbAsync(db => db.Set<Tenant>().Where(t => t.Code == code).Select(t => t.Id).SingleAsync());

    private Task<Guid> LocationIdAsync(string tenantCode, string code) =>
        App.InDbAsync(db => db.Set<Location>().Where(l => l.Code == code && db.Set<Tenant>().Any(t => t.Id == l.TenantId && t.Code == tenantCode)).Select(l => l.Id).SingleAsync());

    private const string Active = "FROM devices.DeviceStates s JOIN devices.Devices d ON d.Id = s.DeviceId WHERE d.Status = 'Active'";

    [Fact]
    public async Task Acme_matches_the_seed_specification()
    {
        var acme = await TenantIdAsync("ACME");
        var where = $"{Active} AND s.TenantId = '{acme}'";

        (await CountAsync($"SELECT COUNT(*) AS Value {where}")).ShouldBe(316);
        (await CountAsync($"SELECT COUNT(*) AS Value {where} AND s.LicenseState = 0")).ShouldBe(26);
        var cairo = await LocationIdAsync("ACME", "CAIRO-HQ");
        (await CountAsync($"SELECT COUNT(*) AS Value {where} AND s.LocationId = '{cairo}' AND s.Health = 3")).ShouldBe(2);
        (await CountAsync($"SELECT COUNT(*) AS Value {where} AND s.LocationId = '{cairo}' AND s.Connection = 0")).ShouldBe(8);
        var fixedNames = await App.InDbAsync(db => db.Database.SqlQueryRaw<string>("SELECT Name AS Value FROM devices.Devices WHERE LocalIp LIKE '192.168.1.%' ").ToListAsync());
        fixedNames.Order().ShouldBe(["APP-SRV-02", "BR-DC-01", "DB-SRV-01", "DESK-01", "DEV-MAC-01", "FILE-SRV-01", "SQL-DB-01", "WEB-SRV-01"]);
        (await CountAsync("SELECT COUNT(*) AS Value FROM devices.InventoryDocuments i JOIN devices.Devices d ON d.Id = i.DeviceId WHERE d.Name = 'WEB-SRV-01' AND d.LocalIp = '192.168.1.10'")).ShouldBe(7);
    }

    [Fact]
    public async Task Platform_dashboard_numbers_equal_sql_counts()
    {
        using var client = await ClientAsync("admin@monitor.local");

        var dashboard = await (await client.GetAsync(new Uri("/api/v1/platform/dashboard", UriKind.Relative))).ShouldBeOkAsync<PlatformDashboardDto>();

        int Tile(string key) => dashboard.Tiles.Single(t => t.Key == key).Value;
        Tile("customers").ShouldBe(await CountAsync("SELECT COUNT(*) AS Value FROM tenancy.Tenants WHERE Status <> 'Archived'"));
        Tile("devices").ShouldBe(await CountAsync($"SELECT COUNT(*) AS Value {Active}"));
        Tile("healthy").ShouldBe(await CountAsync($"SELECT COUNT(*) AS Value {Active} AND s.Health = 1"));
        Tile("warning").ShouldBe(await CountAsync($"SELECT COUNT(*) AS Value {Active} AND s.Health = 2"));
        Tile("critical").ShouldBe(await CountAsync($"SELECT COUNT(*) AS Value {Active} AND s.Health = 3"));
        Tile("licensed").ShouldBe(await CountAsync($"SELECT COUNT(*) AS Value {Active} AND s.LicenseState = 1"));
        Tile("unlicensed").ShouldBe(await CountAsync($"SELECT COUNT(*) AS Value {Active} AND s.LicenseState = 0"));
        dashboard.DeviceHealth.Offline.ShouldBe(await CountAsync($"SELECT COUNT(*) AS Value {Active} AND s.Connection = 0"));
        dashboard.DevicesByOs.Single(o => o.Name == "Linux").Count.ShouldBe(await CountAsync($"SELECT COUNT(*) AS Value {Active} AND s.OsFamily = 2"));
        dashboard.SubscriptionDistribution.Sum(s => s.Count)
            .ShouldBe(await CountAsync("SELECT COUNT(*) AS Value FROM licensing.TenantEntitlements WHERE SubscriptionStatus IN ('Active', 'Trial')"));
        dashboard.TopCustomers[0].Name.ShouldBe("Gulf Engineering");
        dashboard.TopCustomers[0].Devices.ShouldBe(428);
        dashboard.ExpiringSubscriptions.Select(e => e.Name).ShouldContain("Horizon Retail");
        dashboard.IncidentTrend.ShouldAllBe(s => s.Points.Count == 0);
    }

    [Fact]
    public async Task Customer_cards_equal_sql_counts()
    {
        using var client = await ClientAsync("admin@monitor.local");

        var page = await (await client.GetAsync(new Uri("/api/v1/platform/tenants?pageSize=200", UriKind.Relative))).ShouldBeOkAsync<PagedResult<TenantCardDto>>();

        foreach (var card in page.Items.Where(c => c.Code is "ACME" or "GULF" or "C09" or "OASIS"))
        {
            var where = $"{Active} AND s.TenantId = '{card.Id}'";
            card.Devices.ShouldBe(await CountAsync($"SELECT COUNT(*) AS Value {where}"), card.Code);
            card.Healthy.ShouldBe(await CountAsync($"SELECT COUNT(*) AS Value {where} AND s.Health = 1"), card.Code);
            card.Warning.ShouldBe(await CountAsync($"SELECT COUNT(*) AS Value {where} AND s.Health = 2"), card.Code);
            card.Critical.ShouldBe(await CountAsync($"SELECT COUNT(*) AS Value {where} AND s.Health = 3"), card.Code);
            card.LicensesUsed.ShouldBe(await CountAsync($"SELECT COUNT(*) AS Value {where} AND s.LicenseState = 1"), card.Code);
        }

        var critical = await (await client.GetAsync(new Uri("/api/v1/platform/tenants?health=critical&pageSize=200", UriKind.Relative))).ShouldBeOkAsync<PagedResult<TenantCardDto>>();
        critical.Total.ShouldBe(await CountAsync($"SELECT COUNT(DISTINCT s.TenantId) AS Value {Active} AND s.Health = 3 AND s.TenantId IN (SELECT Id FROM tenancy.Tenants WHERE Status <> 'Archived')"));
    }

    [Fact]
    public async Task Customer_dashboard_numbers_equal_sql_counts()
    {
        var acme = await TenantIdAsync("ACME");
        using var client = await ClientAsync("admin@acme.test");

        var dashboard = await (await client.GetAsync(new Uri("/api/v1/dashboard", UriKind.Relative))).ShouldBeOkAsync<TenantDashboardDto>();

        var where = $"{Active} AND s.TenantId = '{acme}'";
        int Tile(string key) => dashboard.Tiles.Single(t => t.Key == key).Value;
        Tile("locations").ShouldBe(3);
        Tile("devices").ShouldBe(await CountAsync($"SELECT COUNT(*) AS Value {where}"));
        Tile("online").ShouldBe(await CountAsync($"SELECT COUNT(*) AS Value {where} AND s.Connection = 1"));
        Tile("healthy").ShouldBe(await CountAsync($"SELECT COUNT(*) AS Value {where} AND s.Health = 1"));
        Tile("warning").ShouldBe(await CountAsync($"SELECT COUNT(*) AS Value {where} AND s.Health = 2"));
        Tile("critical").ShouldBe(await CountAsync($"SELECT COUNT(*) AS Value {where} AND s.Health = 3"));
        Tile("licensed").ShouldBe(await CountAsync($"SELECT COUNT(*) AS Value {where} AND s.LicenseState = 1"));
        Tile("unlicensed").ShouldBe(await CountAsync($"SELECT COUNT(*) AS Value {where} AND s.LicenseState = 0"));
        dashboard.Header.PlanName.ShouldBe("Enterprise");
        dashboard.Header.Devices.ShouldBe(316);

        foreach (var row in dashboard.DeviceStatusByLocation)
        {
            var at = $"{where} AND s.LocationId = '{row.LocationId}'";
            row.Total.ShouldBe(await CountAsync($"SELECT COUNT(*) AS Value {at}"), row.Name);
            row.Online.ShouldBe(await CountAsync($"SELECT COUNT(*) AS Value {at} AND s.Connection = 1"), row.Name);
            row.Critical.ShouldBe(await CountAsync($"SELECT COUNT(*) AS Value {at} AND s.Health = 3"), row.Name);
        }

        dashboard.TopProblematicDevices.Count.ShouldBe(5);
        dashboard.TopProblematicDevices.ShouldAllBe(d => d.Health == "Critical");
        dashboard.License.Licensed.ShouldBe(290);
        dashboard.License.Limit.ShouldBe(500);
    }

    [Fact]
    public async Task Location_dashboard_and_cards_equal_sql_counts()
    {
        var cairo = await LocationIdAsync("ACME", "CAIRO-HQ");
        using var client = await ClientAsync("admin@acme.test");

        var dashboard = await (await client.GetAsync(new Uri($"/api/v1/locations/{cairo}/dashboard", UriKind.Relative))).ShouldBeOkAsync<LocationDashboardDto>();
        var cards = await (await client.GetAsync(new Uri("/api/v1/locations", UriKind.Relative))).ShouldBeOkAsync<PagedResult<LocationCardDto>>();
        var summary = await (await client.GetAsync(new Uri($"/api/v1/devices/summary?locationId={cairo}", UriKind.Relative))).ShouldBeOkAsync<DevicesSummaryDto>();

        var where = $"{Active} AND s.LocationId = '{cairo}'";
        dashboard.Tiles.Single(t => t.Key == "devices").Value.ShouldBe(142);
        dashboard.Tiles.Single(t => t.Key == "online").Value.ShouldBe(134);
        dashboard.Tiles.Single(t => t.Key == "licensed").Value.ShouldBe(138);
        dashboard.DevicesByOs.Select(o => (o.Name, o.Count)).ShouldBe([("Windows", 98), ("Linux", 28), ("MacOS", 12), ("Other", 4)]);
        dashboard.DeviceHealth.ShouldBe(new HealthBreakdownDto(142, 126, 6, 2, 8));
        dashboard.Resources.OnlineDevices.ShouldBe(134);
        var cpuSql = $"SELECT CAST(ROUND(AVG(s.CpuPercent), 1) AS decimal(5,1)) AS Value {where} AND s.Connection = 1";
        var averageCpu = await App.InDbAsync(db => db.Database.SqlQueryRaw<decimal>(cpuSql).SingleAsync());
        dashboard.Resources.Cpu.ShouldBe(averageCpu);
        dashboard.Location.CustomerName.ShouldBe("Acme Corporation");
        // The two critical devices of Cairo HQ come first, then warnings.
        dashboard.TopProblematicDevices.Take(2).Select(d => d.Name).ShouldBe(["SQL-DB-01", "WEB-SRV-01"], ignoreOrder: true);
        dashboard.TopProblematicDevices.Skip(2).ShouldAllBe(d => d.Health == "Warning");

        var card = cards.Items.Single(c => c.Id == cairo);
        card.Devices.ShouldBe(142);
        card.Warning.ShouldBe(6);
        card.HealthScore.ShouldBe(Math.Round(100m * 126 / 142, 1));
        summary.ShouldBe(new DevicesSummaryDto(142, 134, 8, 138, 4, 126, 6, 2, summary.NewDevices));
        summary.NewDevices.ShouldBe(await CountAsync($"SELECT COUNT(*) AS Value {where} AND d.EnrolledAt >= DATEADD(day, -30, CAST('{App.Clock.GetUtcNow():O}' AS datetimeoffset))"));
    }

    [Fact]
    public async Task Device_list_p95_is_below_150_ms_over_100_requests()
    {
        using var client = await ClientAsync("admin@acme.test");
        var cairo = await LocationIdAsync("ACME", "CAIRO-HQ");
        string[] queries = ["?pageSize=24", $"?locationId={cairo}&pageSize=24", "?status=online&sort=name&pageSize=24", "?search=SRV&pageSize=24", "?os=linux&sort=cpu&pageSize=24"];
        for (var i = 0; i < 10; i++)
            (await client.GetAsync(new Uri("/api/v1/devices" + queries[i % queries.Length], UriKind.Relative))).EnsureSuccessStatusCode();

        var timings = new List<double>();
        for (var i = 0; i < 100; i++)
        {
            var watch = Stopwatch.StartNew();
            using var response = await client.GetAsync(new Uri("/api/v1/devices" + queries[i % queries.Length], UriKind.Relative));
            response.EnsureSuccessStatusCode();
            await response.Content.ReadAsByteArrayAsync();
            timings.Add(watch.Elapsed.TotalMilliseconds);
        }

        var p95 = timings.Order().ElementAt(94);
        p95.ShouldBeLessThan(150, $"p95 = {p95:0.0} ms");
    }
}
