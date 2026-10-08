using System.Net;
using Microsoft.EntityFrameworkCore;
using MonitorCloud.Domain.Identity;
using MonitorCloud.Domain.Tenancy;
using MonitorCloud.TestShared;
using MonitorCloud.TestShared.Builders;

namespace MonitorCloud.IntegrationTests.Endpoints;

/// <summary>Tenant B calling every tenant endpoint with tenant A's ids gets 404, never 403, never data; A stays unchanged.</summary>
[Collection(SqlCollection.Name)]
public sealed class CrossTenantTests(SqlServerFixture sql) : EndpointSuiteBase(sql)
{
    /// <summary>Which of tenant A's ids goes into each parameterised route.</summary>
    private static readonly Dictionary<string, Func<TestTenant, Guid>> RouteIds = new(StringComparer.Ordinal)
    {
        ["/api/v1/locations/{id:guid}"] = t => t.Location1.Id,
        ["/api/v1/users/{id:guid}"] = t => t.ItManager.Id,
        ["/api/v1/users/{id:guid}/activate"] = t => t.ItManager.Id,
        ["/api/v1/users/{id:guid}/deactivate"] = t => t.ItManager.Id,
        ["/api/v1/users/{id:guid}/resend-invitation"] = t => t.ItManager.Id,
        ["/api/v1/devices/{id:guid}"] = t => t.Device1.Id,
        ["/api/v1/devices/{id:guid}/retire"] = t => t.Device1.Id,
        ["/api/v1/devices/{id:guid}/unlicense"] = t => t.Device1.Id,
        ["/api/v1/devices/{id:guid}/overview"] = t => t.Device1.Id,
        ["/api/v1/devices/{id:guid}/metrics"] = t => t.Device1.Id,
        ["/api/v1/devices/{id:guid}/disks"] = t => t.Device1.Id,
        ["/api/v1/devices/{id:guid}/inventory/{kind}"] = t => t.Device1.Id,
        ["/api/v1/devices/{id:guid}/live-sessions"] = t => t.Device1.Id,
        ["/api/v1/locations/{id:guid}/dashboard"] = t => t.Location1.Id,
        ["/api/v1/alerts/{id:guid}"] = t => t.Alert1.Id,
        ["/api/v1/alerts/{id:guid}/acknowledge"] = t => t.Alert1.Id,
        ["/api/v1/alerts/{id:guid}/resolve"] = t => t.Alert1.Id,
        ["/api/v1/devices/{id:guid}/alerts"] = t => t.Device1.Id,
        ["/api/v1/devices/{id:guid}/monitor-points"] = t => t.Device1.Id,
        ["/api/v1/settings/alerts/recipients/{id:guid}"] = t => t.Recipient1.Id,
        ["/api/v1/locations/{id:guid}/enrollment-codes"] = t => t.Location1.Id,
        ["/api/v1/locations/{id:guid}/enrollment-codes/{codeId:guid}"] = t => t.Location1.Id,
    };

    private IEnumerable<ApiEndpoint> TenantEndpointsWithIds => BusinessEndpoints.Where(e => e.IsTenant && e.HasParameters);

    [Fact]
    public void Every_parameterised_tenant_endpoint_has_an_id_resolver()
    {
        var missing = TenantEndpointsWithIds.Where(e => !RouteIds.ContainsKey(e.Route)).Select(e => e.Key).ToList();

        missing.ShouldBeEmpty("Add the route to CrossTenantTests.RouteIds.");
    }

    [Fact]
    public async Task Ids_of_another_tenant_are_not_found()
    {
        using var client = App.ClientFor(World.B.Administrator);
        var failures = new List<string>();

        foreach (var endpoint in TenantEndpointsWithIds)
        {
            var id = RouteIds[endpoint.Route](World.A);
            using var request = Request(endpoint, id);
            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            // Validation may answer first for an empty body; what must never happen is 403, 2xx or A's data.
            if (response.StatusCode is not (HttpStatusCode.NotFound or HttpStatusCode.BadRequest) || body.Contains(World.A.Tenant.Name, StringComparison.Ordinal))
                failures.Add($"{endpoint} -> {(int)response.StatusCode} {body}");
        }

        failures.ShouldBeEmpty();
    }

    [Fact]
    public async Task Writes_with_valid_bodies_and_ids_of_another_tenant_are_not_found_and_change_nothing()
    {
        using var client = App.ClientFor(World.B.Administrator);
        var location = World.A.Location1;

        (await client.PutJsonAsync($"/api/v1/locations/{location.Id}", new { name = "Hijacked", code = "HIJACK", timeZone = "Africa/Cairo" }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.DeleteAsync(new Uri($"/api/v1/locations/{location.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.PutJsonAsync($"/api/v1/users/{World.A.ItManager.Id}", new { fullName = "Hijacked", role = Roles.Administrator }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.PostAsync(new Uri($"/api/v1/users/{World.A.ItManager.Id}/deactivate", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var unchanged = await App.InDbAsync(db => db.Set<Location>().SingleAsync(l => l.Id == location.Id));
        unchanged.Name.ShouldBe(location.Name);
        var user = await App.InDbAsync(db => db.Set<User>().SingleAsync(u => u.Id == World.A.ItManager.Id));
        user.Status.ShouldBe(UserStatus.Active);
        user.Role.ShouldBe(Roles.ITManager);
    }

    [Fact]
    public async Task List_endpoints_never_contain_rows_of_another_tenant()
    {
        using var client = App.ClientFor(World.B.Administrator);
        var aIds = new[] { World.A.Id, World.A.Location1.Id, World.A.Location2.Id, World.A.DefaultLocation.Id, World.A.Administrator.Id, World.A.Device1.Id, World.A.Device2.Id }
            .Select(id => id.ToString()).ToList();
        var failures = new List<string>();

        foreach (var endpoint in BusinessEndpoints.Where(e => e.IsTenant && e.Method == "GET" && !e.HasParameters))
        {
            using var response = await client.GetAsync(new Uri(endpoint.Route + "?pageSize=200", UriKind.Relative));
            var body = await response.Content.ReadAsStringAsync();
            response.StatusCode.ShouldBe(HttpStatusCode.OK, endpoint.Key);
            if (aIds.Any(id => body.Contains(id, StringComparison.OrdinalIgnoreCase)) || body.Contains(World.A.Tenant.Name, StringComparison.Ordinal))
                failures.Add(endpoint.Key);
        }

        failures.ShouldBeEmpty();
    }
}
