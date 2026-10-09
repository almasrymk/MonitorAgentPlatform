using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Devices.Token;
using MonitorCloud.Application.Identity;
using MonitorCloud.Domain.Devices;
using MonitorCloud.TestShared;
using MonitorCloud.TestShared.Builders;

namespace MonitorCloud.IntegrationTests.Features;

/// <summary>
/// Key rotation (docs/operations.md section 5): with the old keys in <c>Jwt:Previous*SigningKeys</c>, existing access
/// tokens, refresh tokens, invitations and device secrets keep working; device secrets are re-hashed with the new key, so
/// they survive the removal of the old key.
/// </summary>
[Collection(SqlCollection.Name)]
public sealed class KeyRotationTests(SqlServerFixture sql) : FeatureTestBase(sql)
{
    private readonly SqlServerFixture _sql = sql;

    private const string NewUserKey = "TEST-ONLY-rotated-user-key-0123456789abcdef";
    private const string NewDeviceKey = "TEST-ONLY-rotated-device-key-0123456789abcdef";

    /// <summary>The same database as <see cref="FeatureTestBase.App"/>, started with new keys.</summary>
    private sealed class RotatedApp(SqlServerFixture fixture, string connectionString, bool keepPrevious) : TestApp(fixture)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("ConnectionStrings:Monitor", connectionString);
            builder.UseSetting("Jwt:SigningKey", NewUserKey);
            builder.UseSetting("Jwt:DeviceSigningKey", NewDeviceKey);
            if (keepPrevious)
            {
                builder.UseSetting("Jwt:PreviousSigningKeys:0", UserSigningKey);
                builder.UseSetting("Jwt:PreviousDeviceSigningKeys:0", DeviceSigningKey);
            }
        }
    }

    [Fact]
    public async Task Sessions_invitations_and_agents_survive_a_rotation_of_both_keys()
    {
        using var anonymous = App.CreateClient();
        var session = await (await anonymous.PostJsonAsync("/api/v1/auth/login", new { email = World.A.Administrator.Email, password = TestWorld.Password })).ShouldBeOkAsync<AuthResultDto>();
        using var admin = App.ClientFor(World.A.Administrator);
        (await admin.PostJsonAsync("/api/v1/users", new { fullName = "Rotated Invitee", email = "rotated@alpha.test", role = "Technician", locationIds = Array.Empty<Guid>() })).EnsureSuccessStatusCode();
        var invitation = App.Mail.LastInvitationToken("rotated@alpha.test");
        var oldHash = await App.InDbAsync(db => db.Set<DeviceCredential>().Where(c => c.DeviceId == World.A.Device1.Id && c.RevokedAt == null).Select(c => c.SecretHash).SingleAsync());

        await using (var rotated = new RotatedApp(_sql, App.ConnectionString, keepPrevious: true))
        {
            await rotated.InitializeAsync();
            using var client = rotated.CreateClient();

            using (var old = rotated.ClientWithToken(session.AccessToken))
                (await old.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK, "an access token of the old key is still valid");
            var refreshed = await (await client.PostJsonAsync("/api/v1/auth/refresh", new { refreshToken = session.RefreshToken })).ShouldBeOkAsync<AuthResultDto>();
            using (var renewed = rotated.ClientWithToken(refreshed.AccessToken))
                (await renewed.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await client.PostJsonAsync("/api/v1/auth/invitations/accept", new { token = invitation, password = "Chosen-Pass#2026" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            await (await client.PostJsonAsync("/api/agent/v1/token", new { deviceId = World.A.Device1.Id, deviceSecret = TestWorld.DeviceSecret })).ShouldBeOkAsync<DeviceTokenResult>();
        }

        (await App.InDbAsync(db => db.Set<DeviceCredential>().Where(c => c.DeviceId == World.A.Device1.Id && c.RevokedAt == null).Select(c => c.SecretHash).SingleAsync()))
            .ShouldNotBe(oldHash, "the secret was re-hashed with the new device key");

        await using (var cleaned = new RotatedApp(_sql, App.ConnectionString, keepPrevious: false))
        {
            await cleaned.InitializeAsync();
            using var client = cleaned.CreateClient();

            using (var old = cleaned.ClientWithToken(session.AccessToken))
                (await old.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized, "the old key is gone");
            await (await client.PostJsonAsync("/api/agent/v1/token", new { deviceId = World.A.Device1.Id, deviceSecret = TestWorld.DeviceSecret })).ShouldBeOkAsync<DeviceTokenResult>();
            // Device2 never exchanged a token while the old key was kept: why the old device key stays until every agent did.
            await (await client.PostJsonAsync("/api/agent/v1/token", new { deviceId = World.A.Device2.Id, deviceSecret = TestWorld.DeviceSecret }))
                .ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "DEVICE_INVALID_CREDENTIAL");
        }
    }
}
