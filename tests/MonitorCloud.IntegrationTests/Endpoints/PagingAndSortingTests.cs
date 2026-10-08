using System.Net;
using System.Text.Json;
using MonitorCloud.TestShared;

namespace MonitorCloud.IntegrationTests.Endpoints;

/// <summary>Every list endpoint honours page/pageSize, caps at 200 and rejects unknown sort values.</summary>
[Collection(SqlCollection.Name)]
public sealed class PagingAndSortingTests(SqlServerFixture sql) : EndpointSuiteBase(sql)
{
    private HttpClient ClientFor(ApiEndpoint endpoint) =>
        App.ClientFor(World.PlatformAdmin, endpoint.IsTenant ? World.A.Id : null);

    private IEnumerable<ApiEndpoint> Lists => BusinessEndpoints.Where(e => e.IsList);

    [Fact]
    public void List_endpoints_are_marked()
    {
        Lists.Count().ShouldBeGreaterThanOrEqualTo(5);
        BusinessEndpoints.Where(e => e.Method == "GET" && !e.HasParameters && !e.IsList)
            .Select(e => e.Key)
            .ShouldBe(
                ["GET /api/v1/auth/me", "GET /api/v1/platform/tenants/summary", "GET /api/v1/roles", "GET /api/v1/subscription", "GET /api/v1/platform/plans", "GET /api/v1/platform/licensing/status",
                 "GET /api/v1/devices/summary", "GET /api/v1/dashboard", "GET /api/v1/platform/dashboard",
                 "GET /api/v1/notifications/unread-count", "GET /api/v1/platform/notifications/unread-count", "GET /api/v1/settings/general", "GET /api/v1/settings/alerts",
                 "GET /api/v1/settings/alerts/recipients"],
                ignoreOrder: true,
                customMessage: "A GET collection endpoint must be paged and marked [ListEndpoint], or be listed here.");
    }

    [Fact]
    public async Task Page_size_is_honoured_and_capped_at_200()
    {
        var failures = new List<string>();
        foreach (var endpoint in Lists)
        {
            using var client = ClientFor(endpoint);
            var one = JsonDocument.Parse(await client.GetStringAsync(new Uri(endpoint.Route + "?page=1&pageSize=1", UriKind.Relative))).RootElement;
            var many = JsonDocument.Parse(await client.GetStringAsync(new Uri(endpoint.Route + "?page=1&pageSize=500", UriKind.Relative))).RootElement;
            var second = JsonDocument.Parse(await client.GetStringAsync(new Uri(endpoint.Route + "?page=2&pageSize=1", UriKind.Relative))).RootElement;

            if (one.GetProperty("pageSize").GetInt32() != 1 || one.GetProperty("items").GetArrayLength() > 1)
                failures.Add($"{endpoint}: pageSize=1 not honoured");
            if (many.GetProperty("pageSize").GetInt32() != 200)
                failures.Add($"{endpoint}: pageSize not capped at 200");
            if (second.GetProperty("page").GetInt32() != 2)
                failures.Add($"{endpoint}: page not honoured");
            if (one.GetProperty("total").GetInt32() > 1 && one.GetProperty("items")[0].ToString() == second.GetProperty("items")[0].ToString())
                failures.Add($"{endpoint}: page 2 repeats page 1");
        }

        failures.ShouldBeEmpty();
    }

    [Fact]
    public async Task Unknown_sort_values_are_rejected()
    {
        var failures = new List<string>();
        foreach (var endpoint in Lists)
        {
            using var client = ClientFor(endpoint);
            using var response = await client.GetAsync(new Uri(endpoint.Route + "?sort=Name;DROP%20TABLE", UriKind.Relative));
            var body = await response.Content.ReadAsStringAsync();
            if (response.StatusCode != HttpStatusCode.BadRequest || !body.Contains("VALIDATION_FAILED", StringComparison.Ordinal))
                failures.Add($"{endpoint} -> {(int)response.StatusCode}");
        }

        failures.ShouldBeEmpty();
    }
}
