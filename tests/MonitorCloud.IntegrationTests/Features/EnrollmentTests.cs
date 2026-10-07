using Microsoft.IdentityModel.JsonWebTokens;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MonitorCloud.Application.Devices.Enroll;
using MonitorCloud.Application.Devices.Token;
using MonitorCloud.Domain.Devices;
using MonitorCloud.Domain.Licensing;
using MonitorCloud.Domain.Tenancy;
using MonitorCloud.Infrastructure.Licensing.Fake;
using MonitorCloud.TestShared;
using MonitorCloud.TestShared.Builders;

namespace MonitorCloud.IntegrationTests.Features;

/// <summary>Enrollment = activation (05 section 1, 09 section 2), driven through <c>POST /api/agent/v1/enroll</c>.</summary>
[Collection(SqlCollection.Name)]
public sealed class EnrollmentTests(SqlServerFixture sql) : FeatureTestBase(sql)
{
    private FakeLicensingStore Licensing => App.Services.GetRequiredService<FakeLicensingStore>();

    private FakeLicense LicenseOf(TestTenant tenant) => Licensing.Snapshot().Licenses.Single(l => l.CustomerId == World.LicensingCustomerOf(tenant));

    private static object Body(string key, string fingerprint, string hostname = "WEB-SRV-01", string? locationCode = null, int protocol = 1, string os = "Windows") =>
        new { productKey = key, fingerprint, hostname, osFamily = os, osName = "Windows Server 2022", osVersion = "10.0.20348", architecture = "x64", agentVersion = "1.1.0", protocolVersion = protocol, locationCode, localIp = "10.0.0.5" };

    private async Task<HttpResponseMessage> EnrollAsync(object body)
    {
        using var client = App.CreateClient();
        return await client.PostJsonAsync("/api/agent/v1/enroll", body);
    }

    private async Task<EnrollmentResult> EnrollOkAsync(object body) => await (await EnrollAsync(body)).ShouldBeOkAsync<EnrollmentResult>();

    [Fact]
    public async Task A_new_device_is_created_in_the_default_location_with_a_licence_and_a_secret()
    {
        var license = LicenseOf(World.A);

        var result = await EnrollOkAsync(Body(license.ProductKey, "ma-new-device-0001"));

        result.TenantName.ShouldBe(World.A.Tenant.Name);
        result.LocationName.ShouldBe("Unassigned");
        result.DeviceSecret.Length.ShouldBeGreaterThanOrEqualTo(43);
        result.TokenUrl.ShouldBe("/api/agent/v1/token");
        result.License.State.ShouldBe("Licensed");
        result.License.Token.ShouldNotBeNullOrEmpty();
        result.License.SigningKeys!.Value.GetProperty("keys").GetArrayLength().ShouldBeGreaterThan(0);
        result.CommandSigningKeys.GetProperty("keys").ValueKind.ShouldBe(JsonValueKind.Array);

        var (device, state, seat, credential) = await App.InDbAsync(async db => (
            await db.Set<Device>().SingleAsync(d => d.Id == result.DeviceId),
            await db.Set<DeviceState>().SingleAsync(s => s.DeviceId == result.DeviceId),
            await db.Set<DeviceLicense>().SingleAsync(l => l.DeviceId == result.DeviceId),
            await db.Set<DeviceCredential>().SingleAsync(c => c.DeviceId == result.DeviceId)));
        device.TenantId.ShouldBe(World.A.Id);
        device.LocationId.ShouldBe(World.A.DefaultLocation.Id);
        device.Name.ShouldBe("WEB-SRV-01");
        state.LicenseState.ShouldBe(LicenseStateValue.Licensed);
        seat.LicenseId.ShouldBe(license.Id);
        seat.DeviceFingerprint.ShouldBe("ma-new-device-0001");
        credential.SecretHash.ShouldNotBe(result.DeviceSecret);
        LicenseOf(World.A).Devices.ShouldContain("ma-new-device-0001");
    }

    [Fact]
    public async Task A_location_code_places_the_device_and_counts_one_use()
    {
        var result = await EnrollOkAsync(Body(LicenseOf(World.A).ProductKey, "ma-coded-device-01", locationCode: "loc-alpha-test"));

        result.LocationName.ShouldBe(World.A.Location1.Name);
        var uses = await App.InDbAsync(db => db.Set<LocationEnrollmentCode>().Where(c => c.Id == World.A.EnrollmentCodeId).Select(c => c.Uses).SingleAsync());
        uses.ShouldBe(1);
    }

    [Fact]
    public async Task An_unknown_location_code_fails_and_releases_the_seat_again()
    {
        var response = await EnrollAsync(Body(LicenseOf(World.A).ProductKey, "ma-bad-code-0001", locationCode: "LOC-NOPE00-NOPE00"));

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "ENROLL_INVALID_LOCATION_CODE");
        LicenseOf(World.A).Devices.ShouldNotContain("ma-bad-code-0001");
        (await App.InDbAsync(db => db.Set<Device>().AnyAsync(d => d.Fingerprint == "ma-bad-code-0001"))).ShouldBeFalse();
    }

    [Fact]
    public async Task Another_tenants_location_code_is_not_valid()
    {
        var response = await EnrollAsync(Body(LicenseOf(World.A).ProductKey, "ma-other-code-001", locationCode: "LOC-BETA-TEST"));

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "ENROLL_INVALID_LOCATION_CODE");
    }

    [Fact]
    public async Task Re_enrolling_the_same_fingerprint_reuses_the_device_and_revokes_the_old_secret()
    {
        var key = LicenseOf(World.A).ProductKey;
        var first = await EnrollOkAsync(Body(key, "ma-reinstall-0001"));

        var second = await EnrollOkAsync(Body(key, "ma-reinstall-0001", hostname: "WEB-SRV-01-NEW"));

        second.DeviceId.ShouldBe(first.DeviceId);
        second.DeviceSecret.ShouldNotBe(first.DeviceSecret);
        using var client = App.CreateClient();
        await (await client.PostJsonAsync("/api/agent/v1/token", new { deviceId = first.DeviceId, deviceSecret = first.DeviceSecret }))
            .ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "DEVICE_INVALID_CREDENTIAL");
        await (await client.PostJsonAsync("/api/agent/v1/token", new { deviceId = second.DeviceId, deviceSecret = second.DeviceSecret })).ShouldBeOkAsync<DeviceTokenResult>();
        (await App.InDbAsync(db => db.Set<Device>().CountAsync(d => d.Fingerprint == "ma-reinstall-0001"))).ShouldBe(1);
        LicenseOf(World.A).Devices.Count(d => d == "ma-reinstall-0001").ShouldBe(1);
    }

    [Fact]
    public async Task A_retired_device_is_reactivated_in_its_previous_location()
    {
        var device = World.A.Device1;
        using (var admin = App.ClientFor(World.A.Administrator))
            (await admin.PostAsync(new Uri($"/api/v1/devices/{device.Id}/retire", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var result = await EnrollOkAsync(Body(LicenseOf(World.A).ProductKey, device.Fingerprint));

        result.DeviceId.ShouldBe(device.Id);
        result.LocationName.ShouldBe(World.A.Location1.Name);
        var reloaded = await App.InDbAsync(db => db.Set<Device>().SingleAsync(d => d.Id == device.Id));
        reloaded.Status.ShouldBe(DeviceStatus.Active);
    }

    [Fact]
    public async Task A_suspended_tenant_cannot_enroll_and_the_seat_is_released()
    {
        using (var platform = App.ClientFor(World.PlatformAdmin))
            (await platform.PostJsonAsync($"/api/v1/platform/tenants/{World.B.Id}/suspend", new { reason = "Unpaid invoice" })).IsSuccessStatusCode.ShouldBeTrue();

        var response = await EnrollAsync(Body(LicenseOf(World.B).ProductKey, "ma-suspended-0001"));

        await response.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "ENROLL_TENANT_NOT_ACTIVE");
        LicenseOf(World.B).Devices.ShouldNotContain("ma-suspended-0001");
    }

    [Fact]
    public async Task A_new_licensing_customer_gets_a_tenant_with_a_default_location()
    {
        var customerId = Guid.CreateVersion7();
        var key = TestLicensing.NewKey();
        Licensing.Mutate(data => data.AddCustomer(customerId, "Gamma Provisioned Company", "STARTER", App.Clock.GetUtcNow(), productKey: key));

        var result = await EnrollOkAsync(Body(key, "ma-gamma-device-01"));

        result.TenantName.ShouldBe("Gamma Provisioned Company");
        result.LocationName.ShouldBe("Unassigned");
        var tenant = await App.InDbAsync(db => db.Set<Tenant>().SingleAsync(t => t.LicensingCustomerId == customerId));
        tenant.Status.ShouldBe(TenantStatus.Active);
        (await App.InDbAsync(db => db.Set<Location>().CountAsync(l => l.TenantId == tenant.Id && l.IsDefault))).ShouldBe(1);
    }

    [Fact]
    public async Task Licensing_unavailable_answers_503_and_creates_nothing()
    {
        Licensing.Unavailable = true;

        var response = await EnrollAsync(Body(LicenseOf(World.A).ProductKey, "ma-unavailable-01"));

        await response.ShouldBeProblemAsync(HttpStatusCode.ServiceUnavailable, "LICENSING_UNAVAILABLE");
        Licensing.Unavailable = false;
        (await App.InDbAsync(db => db.Set<Device>().AnyAsync(d => d.Fingerprint == "ma-unavailable-01"))).ShouldBeFalse();
    }

    public static TheoryData<string, HttpStatusCode, string> LicensingFailures => new()
    {
        { "unknown-key", HttpStatusCode.NotFound, "LIC_INVALID_LICENSE" },
        { "suspended", HttpStatusCode.Forbidden, "LIC_SUSPENDED" },
        { "revoked", HttpStatusCode.Forbidden, "LIC_REVOKED" },
        { "expired", HttpStatusCode.Forbidden, "LIC_EXPIRED" },
        { "limit", HttpStatusCode.Conflict, "LIC_ACTIVATION_LIMIT_REACHED" },
    };

    [Theory]
    [MemberData(nameof(LicensingFailures))]
    public async Task Licensing_codes_are_passed_through_unchanged(string scenario, HttpStatusCode status, string code)
    {
        var license = LicenseOf(World.B);
        var key = scenario == "unknown-key" ? TestLicensing.NewKey() : license.ProductKey;
        Licensing.Mutate(data =>
        {
            var l = data.Licenses.Single(x => x.Id == license.Id);
            switch (scenario)
            {
                case "suspended":
                    l.Status = "Suspended";
                    break;
                case "revoked":
                    l.Status = "Revoked";
                    break;
                case "expired":
                    l.ExpiresAt = App.Clock.GetUtcNow().AddDays(-1);
                    break;
                case "limit":
                    l.MaxActivations = l.Devices.Count;
                    break;
            }
        });

        var response = await EnrollAsync(Body(key, "ma-lic-failure-01"));

        await response.ShouldBeProblemAsync(status, code);
        var attempt = await App.InDbAsync(db => db.Set<EnrollmentAttempt>().SingleAsync(a => a.DeviceFingerprint == "ma-lic-failure-01"));
        attempt.Succeeded.ShouldBeFalse();
        attempt.ErrorCode.ShouldBe(code);
        attempt.KeyPrefix.Length.ShouldBe(6);
    }

    [Fact]
    public async Task Unsupported_protocol_versions_are_rejected()
    {
        var response = await EnrollAsync(Body(LicenseOf(World.A).ProductKey, "ma-protocol-0001", protocol: 9));

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "ENROLL_PROTOCOL_UNSUPPORTED");
    }

    [Fact]
    public async Task Invalid_bodies_fail_validation()
    {
        var response = await EnrollAsync(new { productKey = "", fingerprint = "bad fp!", hostname = "" });

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        problem.GetRawText().ShouldContain("fingerprint", Case.Insensitive);
    }

    [Fact]
    public async Task A_fingerprint_may_enroll_five_times_per_hour()
    {
        var key = LicenseOf(World.A).ProductKey;
        for (var i = 0; i < 5; i++)
            (await EnrollAsync(Body(key, "ma-rate-limit-01"))).StatusCode.ShouldBe(HttpStatusCode.OK);

        var sixth = await EnrollAsync(Body(key, "ma-rate-limit-01"));

        await sixth.ShouldBeProblemAsync(HttpStatusCode.TooManyRequests, "RATE_LIMITED");
    }

    [Fact]
    public async Task Successful_attempts_are_logged_without_the_product_key()
    {
        var key = LicenseOf(World.A).ProductKey;

        await EnrollOkAsync(Body(key, "ma-attempt-log-01"));

        var attempts = await App.InDbAsync(db => db.Set<EnrollmentAttempt>().Where(a => a.DeviceFingerprint == "ma-attempt-log-01").ToListAsync());
        attempts.ShouldHaveSingleItem().Succeeded.ShouldBeTrue();
        attempts[0].TenantId.ShouldBe(World.A.Id);
        attempts[0].KeyPrefix.ShouldBe(key.Replace("-", string.Empty, StringComparison.Ordinal)[..6]);
        var audit = await App.InDbAsync(db => db.Set<MonitorCloud.Domain.Audit.AuditRecord>().Where(a => a.Action == "device.enrolled").Select(a => a.Details).ToListAsync());
        audit.ShouldAllBe(d => d == null || !d.Contains(key));
    }
}

/// <summary>Device credentials: token exchange, failures, blocking and rotation (05 section 1.2).</summary>
[Collection(SqlCollection.Name)]
public sealed class DeviceCredentialFlowTests(SqlServerFixture sql) : FeatureTestBase(sql)
{
    private async Task<HttpResponseMessage> TokenAsync(Guid deviceId, string secret)
    {
        using var client = App.CreateClient();
        return await client.PostJsonAsync("/api/agent/v1/token", new { deviceId, deviceSecret = secret });
    }

    [Fact]
    public async Task The_secret_is_exchanged_for_a_60_minute_device_token()
    {
        var device = World.A.Device1;

        var token = await (await TokenAsync(device.Id, TestWorld.DeviceSecret)).ShouldBeOkAsync<DeviceTokenResult>();

        token.ExpiresIn.ShouldBe(3600);
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token.AccessToken);
        jwt.Audiences.ShouldBe(["monitor-agent-gateway"]);
        jwt.GetClaim("typ").Value.ShouldBe("device");
        jwt.GetClaim("sub").Value.ShouldBe(device.Id.ToString());
        jwt.GetClaim("tid").Value.ShouldBe(World.A.Id.ToString());
    }

    [Fact]
    public async Task Unknown_device_wrong_secret_and_retired_device_get_the_same_answer()
    {
        await (await TokenAsync(Guid.CreateVersion7(), TestWorld.DeviceSecret)).ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "DEVICE_INVALID_CREDENTIAL");
        await (await TokenAsync(World.A.Device1.Id, "wrong-secret")).ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "DEVICE_INVALID_CREDENTIAL");
        using (var admin = App.ClientFor(World.A.Administrator))
            (await admin.PostAsync(new Uri($"/api/v1/devices/{World.A.Device2.Id}/retire", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await (await TokenAsync(World.A.Device2.Id, TestWorld.DeviceSecret)).ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "DEVICE_INVALID_CREDENTIAL");
    }

    [Fact]
    public async Task Five_failures_block_the_device_id_for_ten_minutes()
    {
        var device = World.A.Device1;
        for (var i = 0; i < 5; i++)
            await (await TokenAsync(device.Id, "wrong-secret")).ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "DEVICE_INVALID_CREDENTIAL");

        // Even the right secret is refused while the id is blocked.
        await (await TokenAsync(device.Id, TestWorld.DeviceSecret)).ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "DEVICE_INVALID_CREDENTIAL");
        App.Clock.Advance(TimeSpan.FromMinutes(11));
        (await TokenAsync(device.Id, TestWorld.DeviceSecret)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Rotation_issues_a_new_secret_and_the_old_one_stops_working()
    {
        var device = World.A.Device1;
        var token = await (await TokenAsync(device.Id, TestWorld.DeviceSecret)).ShouldBeOkAsync<DeviceTokenResult>();
        using var agent = App.CreateClient();
        using var rotate = new HttpRequestMessage(HttpMethod.Post, "/api/agent/v1/credential/rotate");
        rotate.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        var rotated = await (await agent.SendAsync(rotate)).ShouldBeOkAsync<RotatedSecret>();

        rotated.DeviceSecret.ShouldNotBe(TestWorld.DeviceSecret);
        await (await TokenAsync(device.Id, TestWorld.DeviceSecret)).ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "DEVICE_INVALID_CREDENTIAL");
        (await TokenAsync(device.Id, rotated.DeviceSecret)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Rotation_rejects_user_tokens()
    {
        using var client = App.ClientFor(World.A.Administrator);

        var response = await client.PostAsync(new Uri("/api/agent/v1/credential/rotate", UriKind.Relative), null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
