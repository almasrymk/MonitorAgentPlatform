using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using MonitorCloud.Application.Licensing;
using MonitorCloud.Application.Licensing.Contracts;
using MonitorCloud.Infrastructure.Licensing;
using MonitorCloud.Infrastructure.Licensing.Http;
using MonitorCloud.SharedKernel;
using MonitorCloud.TestShared;

namespace MonitorCloud.UnitTests.Infrastructure;

public sealed class HttpLicensingGatewayTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request, Requests.Count));
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("http://licensing.test/") };
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, status >= HttpStatusCode.BadRequest ? "application/problem+json" : "application/json") };

    private static readonly string TokenBody = """{"accessToken":"client-token","tokenType":"Bearer","expiresIn":3600,"scope":"licenses.activate"}""";

    private static readonly string ActivationBody = """
        {"status":"active","licenseNumber":"LIC-1","productCode":"000001","planCode":"ENTERPRISE","features":["monitoring"],"expiresAt":null,
         "maxActivations":5,"activeActivations":1,"checkAfter":"2026-10-07T08:00:00Z","offlineValidUntil":"2026-10-14T08:00:00Z","token":"jwt","kid":"k1",
         "alreadyActivated":false,"licenseId":"0199b0f0-0000-7000-8000-000000000001","customerId":"0199b0f0-0000-7000-8000-000000000002","subscriptionId":"0199b0f0-0000-7000-8000-000000000003"}
        """;

    private static (HttpLicensingGateway Gateway, StubHandler Handler, TestClock Clock) Create(Func<HttpRequestMessage, int, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        var options = Options.Create(new LicensingSettings { Mode = "Live", BaseUrl = "http://licensing.test/", ClientId = "mc", ClientSecret = "TEST-ONLY-secret", ProductCode = "000001" });
        var clock = new TestClock();
        var cache = new LicensingTokenCache(new Factory(handler), options, clock);
        return (new HttpLicensingGateway(new HttpClient(handler) { BaseAddress = new Uri("http://licensing.test/") }, cache, options), handler, clock);
    }

    [Fact]
    public async Task Activation_sends_the_idempotency_key_and_maps_the_answer()
    {
        var (gateway, handler, _) = Create((r, _) => r.RequestUri!.AbsolutePath.EndsWith("client-token", StringComparison.Ordinal) ? Json(HttpStatusCode.OK, TokenBody) : Json(HttpStatusCode.OK, ActivationBody));

        var result = await gateway.ActivateSeatAsync(new ActivateSeatRequest("KEY", "device-0001", "PC", "1.0", "Windows", "idem-1"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.LicenseId.ShouldBe(Guid.Parse("0199b0f0-0000-7000-8000-000000000001"));
        result.Value.CustomerId.ShouldBe(Guid.Parse("0199b0f0-0000-7000-8000-000000000002"));
        result.Value.PlanCode.ShouldBe("ENTERPRISE");
        var activate = handler.Requests.Single(r => r.RequestUri!.AbsolutePath == "/api/v1/licensing/activate");
        activate.Headers.GetValues("Idempotency-Key").ShouldBe(["idem-1"]);
        activate.Headers.Authorization!.Parameter.ShouldBe("client-token");
    }

    [Fact]
    public async Task Client_token_is_cached_until_a_minute_before_expiry()
    {
        var (gateway, handler, clock) = Create((r, _) => r.RequestUri!.AbsolutePath.EndsWith("client-token", StringComparison.Ordinal) ? Json(HttpStatusCode.OK, TokenBody) : Json(HttpStatusCode.OK, "[]"));

        await gateway.ListPlansAsync(CancellationToken.None);
        await gateway.ListPlansAsync(CancellationToken.None);
        handler.Requests.Count(r => r.RequestUri!.AbsolutePath.EndsWith("client-token", StringComparison.Ordinal)).ShouldBe(1);

        clock.Advance(TimeSpan.FromMinutes(59.5));
        await gateway.ListPlansAsync(CancellationToken.None);
        handler.Requests.Count(r => r.RequestUri!.AbsolutePath.EndsWith("client-token", StringComparison.Ordinal)).ShouldBe(2);
    }

    [Fact]
    public async Task A_401_renews_the_token_and_retries_once()
    {
        var plansCalls = 0;
        var (gateway, handler, _) = Create((r, _) =>
        {
            if (r.RequestUri!.AbsolutePath.EndsWith("client-token", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, TokenBody);
            return ++plansCalls == 1 ? Json(HttpStatusCode.Unauthorized, """{"code":"AUTH_UNAUTHORIZED"}""") : Json(HttpStatusCode.OK, "[]");
        });

        var result = await gateway.ListPlansAsync(CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        handler.Requests.Count(r => r.RequestUri!.AbsolutePath.EndsWith("client-token", StringComparison.Ordinal)).ShouldBe(2);
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, """{"code":"LIC_ACTIVATION_LIMIT_REACHED","title":"limit"}""", "LIC_ACTIVATION_LIMIT_REACHED", ErrorKind.Conflict)]
    [InlineData(HttpStatusCode.NotFound, """{"code":"LIC_INVALID_LICENSE"}""", "LIC_INVALID_LICENSE", ErrorKind.NotFound)]
    [InlineData(HttpStatusCode.Forbidden, """{"code":"LIC_SUSPENDED"}""", "LIC_SUSPENDED", ErrorKind.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests, "", "RATE_LIMITED", ErrorKind.TooManyRequests)]
    [InlineData(HttpStatusCode.BadGateway, "<html/>", "LICENSING_UNAVAILABLE", ErrorKind.Unavailable)]
    public async Task Problem_codes_pass_through_unchanged(HttpStatusCode status, string body, string code, ErrorKind kind)
    {
        var (gateway, _, _) = Create((r, _) => r.RequestUri!.AbsolutePath.EndsWith("client-token", StringComparison.Ordinal) ? Json(HttpStatusCode.OK, TokenBody) : Json(status, body));

        var result = await gateway.ActivateSeatAsync(new ActivateSeatRequest("KEY", "device-0001", "PC", "1.0", "Windows", "idem-1"), CancellationToken.None);

        result.Error!.Code.ShouldBe(code);
        result.Error.Kind.ShouldBe(kind);
    }

    [Fact]
    public async Task Transport_failures_become_LICENSING_UNAVAILABLE()
    {
        var (gateway, _, _) = Create((_, _) => throw new HttpRequestException("connection refused"));

        var result = await gateway.GetEntitlementsAsync(Guid.CreateVersion7(), CancellationToken.None);

        result.Error!.Code.ShouldBe("LICENSING_UNAVAILABLE");
    }

    [Fact]
    public async Task Release_and_change_feed_and_signing_keys_use_the_integration_routes()
    {
        var (gateway, handler, _) = Create((r, _) => r.RequestUri!.AbsolutePath switch
        {
            var p when p.EndsWith("client-token", StringComparison.Ordinal) => Json(HttpStatusCode.OK, TokenBody),
            var p when p.EndsWith("/release", StringComparison.Ordinal) => new HttpResponseMessage(HttpStatusCode.NoContent),
            "/api/v1/integration/changes" => Json(HttpStatusCode.OK, """{"items":[],"nextCursor":"c2","hasMore":false}"""),
            "/api/v1/signing-keys" => Json(HttpStatusCode.OK, """[{"kid":"k1"}]"""),
            _ => Json(HttpStatusCode.OK, """{"items":[],"total":0,"page":1,"pageSize":50}"""),
        });
        var license = Guid.CreateVersion7();

        (await gateway.ReleaseSeatAsync(license, "device-0001", CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await gateway.GetChangesAsync("c1", 10, CancellationToken.None)).Value.NextCursor.ShouldBe("c2");
        (await gateway.GetSigningKeysJsonAsync(CancellationToken.None)).Value.ShouldContain("k1");
        (await gateway.ListCustomersAsync(1, 50, null, CancellationToken.None)).IsSuccess.ShouldBeTrue();

        handler.Requests.ShouldContain(r => r.RequestUri!.PathAndQuery == $"/api/v1/integration/licenses/{license}/devices/device-0001/release");
        handler.Requests.ShouldContain(r => r.RequestUri!.PathAndQuery == "/api/v1/integration/changes?take=10&cursor=c1");
    }

    [Theory]
    [InlineData(400, ErrorKind.Validation)]
    [InlineData(401, ErrorKind.Unauthorized)]
    [InlineData(410, ErrorKind.Gone)]
    [InlineData(423, ErrorKind.Locked)]
    [InlineData(503, ErrorKind.Unavailable)]
    public void Statuses_map_to_error_kinds(int status, ErrorKind kind) => LicensingProblem.FromStatus(status, null, null).Kind.ShouldBe(kind);

    [Theory]
    [InlineData("acme-dev-0001", true)]
    [InlineData("short", false)]
    [InlineData("has space here", false)]
    [InlineData("ok:id.with_parts-1", true)]
    public void Device_ids_follow_the_Licensing_format(string id, bool valid) => LicensingProblem.IsValidDeviceId(id).ShouldBe(valid);

    [Fact]
    public void Feature_codes_are_lower_case_and_dotted() =>
        new[] { Features.Monitoring, Features.Alerts, Features.NotificationsEmail, Features.NotificationsWebhook, Features.MonitorPoints, Features.ReportsBasic, Features.ReportsAdvanced, Features.Archive, Features.RemoteActions }
            .ShouldAllBe(f => string.Equals(f, f.ToLowerInvariant(), StringComparison.Ordinal));
}
