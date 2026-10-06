using System.Net;
using MonitorCloud.TestShared;

namespace MonitorCloud.IntegrationTests.Endpoints;

[Collection(SqlCollection.Name)]
public sealed class AnonymousAccessTests(SqlServerFixture sql) : EndpointSuiteBase(sql)
{
    [Fact]
    public void Only_allow_listed_endpoints_accept_anonymous_callers()
    {
        var offenders = Endpoints.Where(e => e.AllowsAnonymous && !EndpointCatalog.AnonymousAllowList.Contains(e.ToString())).ToList();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public async Task Every_other_endpoint_returns_401_without_a_token()
    {
        using var client = App.CreateClient();
        var failures = new List<string>();

        foreach (var endpoint in Endpoints.Where(e => !EndpointCatalog.AnonymousAllowList.Contains(e.ToString())))
        {
            using var request = Request(endpoint, Guid.CreateVersion7());
            using var response = await client.SendAsync(request);
            if (response.StatusCode != HttpStatusCode.Unauthorized)
                failures.Add($"{endpoint} -> {(int)response.StatusCode}");
        }

        failures.ShouldBeEmpty();
    }

    [Fact]
    public async Task Health_and_openapi_are_reachable_anonymously()
    {
        using var client = App.CreateClient();

        foreach (var path in new[] { "/health/live", "/health/ready", "/openapi/v1.json" })
        {
            using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
            response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        }
    }
}
