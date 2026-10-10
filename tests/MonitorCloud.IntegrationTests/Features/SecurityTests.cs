using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using MonitorCloud.Application.Common;
using MonitorCloud.Application.Devices.Enroll;
using MonitorCloud.Application.Identity;
using MonitorCloud.Domain.Identity;
using MonitorCloud.Domain.Tenancy;
using MonitorCloud.Infrastructure.Licensing.Fake;
using MonitorCloud.TestShared;
using MonitorCloud.TestShared.Builders;
using Serilog.Core;
using Serilog.Events;

namespace MonitorCloud.IntegrationTests.Features;

/// <summary>The security tests of 09 section 9 not covered by the auth, endpoint and upload suites.</summary>
[Collection(SqlCollection.Name)]
public sealed class SecurityTests(SqlServerFixture sql) : FeatureTestBase(sql)
{
    private static string Sign(string header, string payload, string key)
    {
        var unsigned = $"{Base64UrlEncoder.Encode(header)}.{Base64UrlEncoder.Encode(payload)}";
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.ASCII.GetBytes(unsigned));
        return $"{unsigned}.{Base64UrlEncoder.Encode(signature)}";
    }

    private async Task<HttpStatusCode> MeAsync(string token)
    {
        using var client = App.ClientWithToken(token);
        return (await client.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative))).StatusCode;
    }

    [Fact]
    public async Task Tokens_with_a_wrong_key_issuer_audience_or_lifetime_are_rejected()
    {
        var valid = App.TokenFor(World.A.ReportViewer);
        (await MeAsync(valid)).ShouldBe(HttpStatusCode.OK);
        var parts = valid.Split('.');
        var header = Base64UrlEncoder.Decode(parts[0]);
        JsonObject Payload() => JsonNode.Parse(Base64UrlEncoder.Decode(parts[1]))!.AsObject();
        string With(Action<JsonObject> change, string key = TestApp.UserSigningKey)
        {
            var payload = Payload();
            change(payload);
            return Sign(header, payload.ToJsonString(), key);
        }

        // Re-signed with the right key and unchanged: accepted (the forging helper itself is correct).
        (await MeAsync(With(_ => { }))).ShouldBe(HttpStatusCode.OK);
        (await MeAsync(With(_ => { }, "TEST-ONLY-another-key-0123456789abcdef0123"))).ShouldBe(HttpStatusCode.Unauthorized);
        (await MeAsync(With(p => p["iss"] = "someone-else"))).ShouldBe(HttpStatusCode.Unauthorized);
        (await MeAsync(With(p => p["aud"] = "monitor-agent-gateway"))).ShouldBe(HttpStatusCode.Unauthorized);
        var past = App.Clock.GetUtcNow().AddHours(-2).ToUnixTimeSeconds();
        (await MeAsync(With(p => { p["exp"] = past; p["nbf"] = past - 900; p["iat"] = past - 900; }))).ShouldBe(HttpStatusCode.Unauthorized);

        // A changed tenant without the key: the signature no longer matches.
        var tampered = Payload().ToJsonString().Replace(World.A.Id.ToString(), World.B.Id.ToString(), StringComparison.OrdinalIgnoreCase);
        (await MeAsync($"{parts[0]}.{Base64UrlEncoder.Encode(tampered)}.{parts[2]}")).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Extra_tenant_role_and_status_fields_in_bodies_are_ignored()
    {
        using var admin = App.ClientFor(World.A.Administrator);

        var invited = await (await admin.PostJsonAsync("/api/v1/users", new
        {
            fullName = "Mass Assignment", email = "mass@alpha.test", role = Roles.Technician, locationIds = Array.Empty<Guid>(),
            tenantId = World.B.Id, status = "Active", permissions = new[] { "platform.tenants.manage" },
        })).ShouldBeOkAsync<UserListItemDto>(HttpStatusCode.Created);
        invited.Status.ShouldBe("Invited");
        var stored = await App.InDbAsync(db => db.Set<User>().IgnoreQueryFilters().SingleAsync(u => u.Email == "mass@alpha.test"));
        (stored.TenantId, stored.Role, stored.Status).ShouldBe(((Guid?)World.A.Id, Roles.Technician, UserStatus.Invited));

        var location = await (await admin.PostJsonAsync("/api/v1/locations", new { name = "Mass Site", code = "MASS-1", timeZone = "Africa/Cairo", tenantId = World.B.Id, isDefault = true }))
            .ShouldBeOkAsync<MonitorCloud.Application.Tenancy.LocationDto>(HttpStatusCode.Created);
        var row = await App.InDbAsync(db => db.Set<Location>().IgnoreQueryFilters().SingleAsync(l => l.Id == location.Id));
        (row.TenantId, row.IsDefault).ShouldBe((World.A.Id, false));

        using var viewer = App.ClientFor(World.A.ReportViewer);
        (await viewer.PutJsonAsync("/api/v1/auth/me/language", new { language = "ar", role = Roles.Administrator, tenantId = World.B.Id })).EnsureSuccessStatusCode();
        var me = await App.InDbAsync(db => db.Set<User>().IgnoreQueryFilters().SingleAsync(u => u.Id == World.A.ReportViewer.Id));
        (me.Role, me.TenantId).ShouldBe((Roles.ReportViewer, (Guid?)World.A.Id));
    }

    public static TheoryData<string> Probes => new() { "' OR 1=1 --", "'; DROP TABLE devices.Devices; --", "%", "_", "[a-z]%", "\" OR \"\"=\"" };

    [Theory]
    [MemberData(nameof(Probes))]
    public async Task Search_parameters_are_data_never_sql(string probe)
    {
        using var tenant = App.ClientFor(World.A.Administrator);
        using var platform = App.ClientFor(World.PlatformAdmin);
        var q = Uri.EscapeDataString(probe);

        foreach (var (client, url) in new[]
                 {
                     (tenant, $"/api/v1/devices?search={q}"), (tenant, $"/api/v1/users?search={q}"), (tenant, $"/api/v1/locations?search={q}"),
                     (tenant, $"/api/v1/audit?actor={q}"), (tenant, $"/api/v1/audit?action={q}"), (platform, $"/api/v1/platform/tenants?search={q}"),
                     (platform, $"/api/v1/platform/users?search={q}"), (platform, $"/api/v1/platform/audit?actor={q}"),
                 })
        {
            using var response = await client.GetAsync(new Uri(url, UriKind.Relative));
            var body = await response.Content.ReadAsStringAsync();
            response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{url}: {body}");
            JsonDocument.Parse(body).RootElement.GetProperty("total").GetInt32().ShouldBe(0, $"{url} matched rows: the probe was not taken literally");
        }

        (await App.InDbAsync(db => db.Set<MonitorCloud.Domain.Devices.Device>().IgnoreQueryFilters().CountAsync())).ShouldBe(4, "nothing was dropped");
    }
}

/// <summary>Secrets never appear in the logs (09 section 9): every event of a sign-in, refresh, enrollment and token exchange is inspected.</summary>
[Collection(SqlCollection.Name)]
public sealed class SecretsInLogsTests(SqlServerFixture sql) : IAsyncLifetime
{
    private sealed class CapturingSink : ILogEventSink
    {
        public ConcurrentQueue<string> Lines { get; } = new();

        public void Emit(LogEvent logEvent)
        {
            ArgumentNullException.ThrowIfNull(logEvent);
            var properties = string.Join(' ', logEvent.Properties.Select(p => $"{p.Key}={p.Value}"));
            Lines.Enqueue($"{logEvent.RenderMessage()} {properties} {logEvent.Exception}");
        }
    }

    private sealed class LoggingApp(SqlServerFixture fixture, CapturingSink sink) : TestApp(fixture)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Serilog:MinimumLevel:Default"] = "Verbose",
                ["Serilog:MinimumLevel:Override:Microsoft"] = "Verbose",
                ["Serilog:MinimumLevel:Override:System"] = "Verbose",
            }));
        }

        protected override void ConfigureTestServices(IServiceCollection services) => services.AddSingleton<ILogEventSink>(sink);
    }

    private readonly CapturingSink _sink = new();
    private LoggingApp _app = null!;
    private TestWorld _world = null!;

    public async Task InitializeAsync()
    {
        _app = new LoggingApp(sql, _sink);
        await _app.InitializeAsync();
        await _app.ResetDatabaseAsync();
        _world = await TestWorld.CreateAsync(_app);
    }

    public Task DisposeAsync() => _app.DisposeAsync();

    [Fact]
    public async Task Passwords_tokens_product_keys_and_device_secrets_are_never_logged()
    {
        using var client = _app.CreateClient();
        var session = await (await client.PostJsonAsync("/api/v1/auth/login", new { email = _world.A.Administrator.Email, password = TestWorld.Password }))
            .ShouldBeOkAsync<MonitorCloud.Application.Identity.AuthResultDto>();
        var refreshed = await (await client.PostJsonAsync("/api/v1/auth/refresh", new { refreshToken = session.RefreshToken }))
            .ShouldBeOkAsync<MonitorCloud.Application.Identity.AuthResultDto>();
        await client.PostJsonAsync("/api/v1/auth/login", new { email = _world.A.Administrator.Email, password = "Wrong-" + TestWorld.Password });

        var key = _app.Services.GetRequiredService<FakeLicensingStore>().Snapshot().Licenses.Single(l => l.CustomerId == _world.LicensingCustomerOf(_world.A)).ProductKey;
        var enrolled = await (await client.PostJsonAsync("/api/agent/v1/enroll", new
        {
            productKey = key, fingerprint = "logs-device-0001", hostname = "LOG-PC-01", osFamily = "Windows", osName = "Windows 11 Pro", osVersion = "10.0.26100", architecture = "x64",
            agentVersion = "1.1.0", protocolVersion = 1, locationCode = (string?)null, localIp = "10.0.0.9",
        })).ShouldBeOkAsync<EnrollmentResult>();
        var deviceToken = await (await client.PostJsonAsync("/api/agent/v1/token", new { deviceId = enrolled.DeviceId, deviceSecret = enrolled.DeviceSecret }))
            .ShouldBeOkAsync<MonitorCloud.Application.Devices.Token.DeviceTokenResult>();
        await client.PostJsonAsync("/api/agent/v1/token", new { deviceId = enrolled.DeviceId, deviceSecret = "TEST-ONLY-wrong-secret" });

        var secrets = new[]
        {
            TestWorld.Password, "Wrong-" + TestWorld.Password, session.AccessToken, session.RefreshToken, refreshed.RefreshToken, key, enrolled.DeviceSecret,
            deviceToken.AccessToken, "TEST-ONLY-wrong-secret",
        };
        // Request logging writes after the response is sent: wait for the entry of the last request.
        for (var i = 0; i < 50 && !_sink.Lines.Any(l => l.Contains("/api/agent/v1/token", StringComparison.Ordinal) && l.Contains("401", StringComparison.Ordinal)); i++)
            await Task.Delay(100);
        var lines = _sink.Lines.ToList();
        // The sink sees the request log of every call inspected here.
        foreach (var path in new[] { "/api/v1/auth/login", "/api/v1/auth/refresh", "/api/agent/v1/enroll", "/api/agent/v1/token" })
            lines.ShouldContain(l => l.Contains(path, StringComparison.Ordinal), $"the request log of {path} is captured");
        foreach (var secret in secrets)
            lines.Where(l => l.Contains(secret, StringComparison.Ordinal)).ShouldBeEmpty($"a secret ({secret[..Math.Min(6, secret.Length)]}...) was logged");
    }
}

/// <summary>Rate limits on sign-in, enrollment and the device token (09 section 9), with the configured limits lowered.</summary>
[Collection(SqlCollection.Name)]
public sealed class RateLimitTests(SqlServerFixture sql) : IAsyncLifetime
{
    private sealed class LimitedApp(SqlServerFixture fixture) : TestApp(fixture)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("RateLimiting:Auth:PermitLimit", "3");
            builder.UseSetting("RateLimiting:Refresh:PermitLimit", "4");
            builder.UseSetting("RateLimiting:Enroll:PermitLimit", "2");
            builder.UseSetting("RateLimiting:AgentToken:PermitLimit", "2");
        }
    }

    private LimitedApp _app = null!;

    public async Task InitializeAsync()
    {
        _app = new LimitedApp(sql);
        await _app.InitializeAsync();
        await _app.ResetDatabaseAsync();
    }

    public Task DisposeAsync() => _app.DisposeAsync();

    [Theory]
    [InlineData("/api/v1/auth/login", 3)]
    [InlineData("/api/v1/auth/refresh", 4)]
    [InlineData("/api/agent/v1/enroll", 2)]
    [InlineData("/api/agent/v1/token", 2)]
    public async Task The_request_after_the_limit_gets_429_with_Retry_After(string url, int limit)
    {
        using var client = _app.CreateClient();
        for (var i = 0; i < limit; i++)
            (await client.PostJsonAsync(url, new { })).StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);

        using var limited = await client.PostJsonAsync(url, new { });
        await limited.ShouldBeProblemAsync(HttpStatusCode.TooManyRequests, "RATE_LIMITED");
        limited.Headers.RetryAfter.ShouldNotBeNull();
    }
}

/// <summary>Behind a trusted reverse proxy the per-IP limits use the client address of <c>X-Forwarded-For</c>.</summary>
[Collection(SqlCollection.Name)]
public sealed class ReverseProxyTests(SqlServerFixture sql) : IAsyncLifetime
{
    private sealed class ProxiedApp(SqlServerFixture fixture) : TestApp(fixture)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("RateLimiting:Auth:PermitLimit", "2");
            builder.UseSetting("ReverseProxy:KnownNetworks:0", "10.0.0.0/8");
        }
    }

    private ProxiedApp _app = null!;

    public async Task InitializeAsync()
    {
        _app = new ProxiedApp(sql);
        await _app.InitializeAsync();
        await _app.ResetDatabaseAsync();
    }

    public Task DisposeAsync() => _app.DisposeAsync();

    private async Task<int> LoginFromAsync(string proxy, string client)
    {
        var context = await _app.Server.SendAsync(c =>
        {
            c.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(proxy);
            c.Request.Method = "POST";
            c.Request.Path = "/api/v1/auth/login";
            c.Request.Headers["X-Forwarded-For"] = client;
            c.Request.ContentType = "application/json";
            c.Request.Body = new MemoryStream("{}"u8.ToArray());
        });
        return context.Response.StatusCode;
    }

    [Fact]
    public async Task Each_client_behind_the_proxy_has_its_own_limit_and_untrusted_senders_cannot_spoof_it()
    {
        (await LoginFromAsync("10.1.2.3", "203.0.113.7")).ShouldNotBe(429);
        (await LoginFromAsync("10.1.2.3", "203.0.113.7")).ShouldNotBe(429);
        (await LoginFromAsync("10.1.2.3", "203.0.113.7")).ShouldBe(429, "the third sign-in of this client in a minute");
        (await LoginFromAsync("10.1.2.3", "203.0.113.8")).ShouldNotBe(429, "another client behind the same proxy");

        // A sender outside the trusted networks is limited by its own address, whatever it claims.
        (await LoginFromAsync("198.51.100.20", "203.0.113.50")).ShouldNotBe(429);
        (await LoginFromAsync("198.51.100.20", "203.0.113.51")).ShouldNotBe(429);
        (await LoginFromAsync("198.51.100.20", "203.0.113.52")).ShouldBe(429);
    }
}
