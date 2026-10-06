using System.Net;
using System.Text.Json;
using MonitorCloud.TestShared;

namespace MonitorCloud.IntegrationTests.Endpoints;

[Collection(SqlCollection.Name)]
public sealed class ProblemDetailsTests(SqlServerFixture sql) : EndpointSuiteBase(sql)
{
    private static async Task ShouldBeProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("code").GetString().ShouldBe(code);
        body.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
        body.GetProperty("status").GetInt32().ShouldBe((int)status);
    }

    [Fact]
    public async Task Unknown_route_is_a_problem_with_code_and_trace_id()
    {
        using var client = App.ClientFor(World.A.Administrator);

        using var response = await client.GetAsync(new Uri("/api/v1/does-not-exist", UriKind.Relative));

        await ShouldBeProblem(response, HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Fact]
    public async Task Wrong_method_is_a_problem()
    {
        using var client = App.ClientFor(World.A.Administrator);

        using var response = await client.DeleteAsync(new Uri("/openapi/v1.json", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Unknown_route_without_a_token_is_401_so_routes_do_not_leak()
    {
        using var client = App.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/does-not-exist", UriKind.Relative));

        await ShouldBeProblem(response, HttpStatusCode.Unauthorized, "AUTH_UNAUTHORIZED");
    }

    [Fact]
    public async Task Every_business_endpoint_answers_errors_as_problem_details()
    {
        using var client = App.CreateClient();
        var failures = new List<string>();

        foreach (var endpoint in BusinessEndpoints.Where(e => !e.AllowsAnonymous))
        {
            using var request = Request(endpoint, Guid.CreateVersion7());
            using var response = await client.SendAsync(request);
            if (response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType != "application/problem+json")
                failures.Add(endpoint.ToString());
        }

        failures.ShouldBeEmpty();
    }
}
