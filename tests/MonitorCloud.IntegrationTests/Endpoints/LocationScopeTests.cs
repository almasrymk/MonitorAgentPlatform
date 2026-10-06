using System.Net;
using MonitorCloud.Application.Common;
using MonitorCloud.Application.Tenancy;
using MonitorCloud.TestShared;

namespace MonitorCloud.IntegrationTests.Endpoints;

/// <summary>A user restricted to one location sees only that location's data on every endpoint.</summary>
[Collection(SqlCollection.Name)]
public sealed class LocationScopeTests(SqlServerFixture sql) : EndpointSuiteBase(sql)
{
    [Fact]
    public async Task Lists_contain_only_the_users_locations()
    {
        using var client = App.ClientFor(World.A.RestrictedManager);
        var outside = new[] { World.A.Location2.Id, World.A.DefaultLocation.Id }.Select(i => i.ToString()).ToList();
        var failures = new List<string>();

        foreach (var endpoint in BusinessEndpoints.Where(e => e.IsTenant && e.Method == "GET" && !e.HasParameters))
        {
            using var response = await client.GetAsync(new Uri(endpoint.Route + "?pageSize=200", UriKind.Relative));
            if (response.StatusCode == HttpStatusCode.Forbidden)
                continue;
            var body = await response.Content.ReadAsStringAsync();
            if (outside.Any(id => body.Contains(id, StringComparison.OrdinalIgnoreCase)))
                failures.Add(endpoint.Key);
        }

        failures.ShouldBeEmpty();
    }

    [Fact]
    public async Task Locations_list_shows_exactly_the_scoped_location()
    {
        using var client = App.ClientFor(World.A.RestrictedManager);

        var page = await (await client.GetAsync(new Uri("/api/v1/locations", UriKind.Relative))).ShouldBeOkAsync<PagedResult<LocationCardDto>>();

        page.Items.Select(l => l.Id).ShouldBe([World.A.Location1.Id]);
    }

    [Fact]
    public async Task Locations_outside_the_scope_are_not_found_for_reads_and_writes()
    {
        using var client = App.ClientFor(World.A.RestrictedManager);
        var other = World.A.Location2.Id;

        await (await client.GetAsync(new Uri($"/api/v1/locations/{other}", UriKind.Relative))).ShouldBeProblemAsync(HttpStatusCode.NotFound, "LOCATION_NOT_FOUND");
        await (await client.PutJsonAsync($"/api/v1/locations/{other}", new { name = "X", code = "X1", timeZone = "Africa/Cairo" }))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, "LOCATION_NOT_FOUND");
        (await client.GetAsync(new Uri($"/api/v1/locations/{World.A.Location1.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Location_codes_stay_unique_across_locations_hidden_by_the_scope()
    {
        using var client = App.ClientFor(World.A.RestrictedManager);

        await (await client.PostJsonAsync("/api/v1/locations", new { name = "Copy", code = World.A.Location2.Code, timeZone = "Africa/Cairo" }))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "LOCATION_CODE_TAKEN");
    }
}
