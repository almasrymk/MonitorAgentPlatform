using System.Net;
using System.Text.Json;
using MonitorCloud.TestShared;

namespace MonitorCloud.IntegrationTests.Infrastructure;

[Collection(SqlCollection.Name)]
public sealed class HostTests(SqlServerFixture sql) : IAsyncLifetime
{
    private readonly TestApp _app = new(sql);

    public Task InitializeAsync() => _app.InitializeAsync();

    public Task DisposeAsync() => _app.DisposeAsync();

    [Fact]
    public async Task Ready_health_is_healthy_with_sql_server()
    {
        using var client = _app.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.GetProperty("status").GetString().ShouldBe("Healthy");
        body.GetProperty("checks").GetProperty("database").GetProperty("status").GetString().ShouldBe("Healthy");
        body.GetProperty("checks").GetProperty("outbox").GetProperty("status").GetString().ShouldBe("Healthy");
    }

    [Fact]
    public async Task Live_health_does_not_touch_dependencies()
    {
        using var client = _app.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.GetProperty("checks").EnumerateObject().ShouldBeEmpty();
    }

    [Fact]
    public async Task OpenApi_document_is_served_anonymously()
    {
        using var client = _app.CreateClient();

        using var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("openapi").GetString().ShouldStartWith("3.");
    }

    [Fact]
    public async Task Correlation_id_is_echoed_when_valid()
    {
        using var client = _app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Correlation-Id", "abc-123");

        using var response = await client.SendAsync(request);

        response.Headers.GetValues("X-Correlation-Id").ShouldBe(["abc-123"]);
    }

    [Fact]
    public async Task Correlation_id_is_created_when_missing_or_unsafe()
    {
        using var client = _app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", "<script>");

        using var response = await client.SendAsync(request);

        var id = response.Headers.GetValues("X-Correlation-Id").Single();
        id.ShouldNotBe("<script>");
        id.Length.ShouldBe(32);
    }

    [Fact]
    public async Task Security_headers_are_present()
    {
        using var client = _app.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        response.Headers.GetValues("X-Frame-Options").ShouldBe(["DENY"]);
        response.Headers.GetValues("Referrer-Policy").ShouldBe(["no-referrer"]);
        response.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("default-src 'none'");
    }

    [Fact]
    public async Task Cors_allows_only_configured_origins()
    {
        using var client = _app.CreateClient();

        using var allowed = new HttpRequestMessage(HttpMethod.Options, "/health/live");
        allowed.Headers.Add("Origin", "http://localhost:4300");
        allowed.Headers.Add("Access-Control-Request-Method", "GET");
        using var allowedResponse = await client.SendAsync(allowed);

        using var denied = new HttpRequestMessage(HttpMethod.Options, "/health/live");
        denied.Headers.Add("Origin", "https://evil.example");
        denied.Headers.Add("Access-Control-Request-Method", "GET");
        using var deniedResponse = await client.SendAsync(denied);

        allowedResponse.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe(["http://localhost:4300"]);
        deniedResponse.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }
}
