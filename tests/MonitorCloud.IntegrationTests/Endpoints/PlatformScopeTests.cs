using System.Net;
using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Common;
using MonitorCloud.Application.Tenancy;
using MonitorCloud.Domain.Audit;
using MonitorCloud.TestShared;

namespace MonitorCloud.IntegrationTests.Endpoints;

[Collection(SqlCollection.Name)]
public sealed class PlatformScopeTests(SqlServerFixture sql) : EndpointSuiteBase(sql)
{
    [Fact]
    public async Task Tenant_users_get_403_on_every_platform_endpoint()
    {
        using var client = App.ClientFor(World.A.Administrator);
        var failures = new List<string>();

        foreach (var endpoint in BusinessEndpoints.Where(e => e.IsPlatform))
        {
            using var request = Request(endpoint, World.A.Id);
            using var response = await client.SendAsync(request);
            if (response.StatusCode != HttpStatusCode.Forbidden)
                failures.Add($"{endpoint} -> {(int)response.StatusCode}");
        }

        failures.ShouldBeEmpty();
    }

    [Fact]
    public async Task Platform_users_without_a_workspace_get_TENANT_SCOPE_REQUIRED_on_tenant_endpoints()
    {
        using var client = App.ClientFor(World.PlatformAdmin);
        var failures = new List<string>();

        foreach (var endpoint in BusinessEndpoints.Where(e => e.IsTenant))
        {
            using var request = Request(endpoint, Guid.CreateVersion7());
            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (response.StatusCode != HttpStatusCode.BadRequest || !body.Contains("TENANT_SCOPE_REQUIRED", StringComparison.Ordinal))
                failures.Add($"{endpoint} -> {(int)response.StatusCode} {body}");
        }

        failures.ShouldBeEmpty();
    }

    [Fact]
    public async Task Inside_a_workspace_platform_users_see_that_tenant_only()
    {
        using var client = App.ClientFor(World.PlatformSupport, World.A.Id);

        var page = await (await client.GetAsync(new Uri("/api/v1/locations", UriKind.Relative))).ShouldBeOkAsync<PagedResult<LocationCardDto>>();

        page.Items.Select(l => l.Id).ShouldBe([World.A.Location1.Id, World.A.Location2.Id, World.A.DefaultLocation.Id], ignoreOrder: true);
    }

    [Fact]
    public async Task Unknown_workspace_is_not_found()
    {
        using var client = App.ClientFor(World.PlatformAdmin, Guid.CreateVersion7());

        await (await client.GetAsync(new Uri("/api/v1/locations", UriKind.Relative))).ShouldBeProblemAsync(HttpStatusCode.NotFound, "TENANT_NOT_FOUND");
    }

    [Fact]
    public async Task Tenant_user_sending_another_tenants_id_is_rejected_and_audited()
    {
        using var client = App.ClientFor(World.A.Administrator, World.B.Id);

        await (await client.GetAsync(new Uri("/api/v1/locations", UriKind.Relative))).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "AUTH_FORBIDDEN");

        var audit = await App.InDbAsync(db => db.Set<AuditRecord>().Where(a => a.Action == "tenant.cross_attempt").ToListAsync());
        audit.ShouldHaveSingleItem().Success.ShouldBeFalse();
    }

    [Fact]
    public async Task Tenant_user_sending_its_own_tenant_id_is_accepted()
    {
        using var client = App.ClientFor(World.A.Administrator, World.A.Id);

        (await client.GetAsync(new Uri("/api/v1/locations", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
