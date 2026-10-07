using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MonitorCloud.TestShared;

namespace MonitorCloud.IntegrationTests.Endpoints;

/// <summary>A device token is rejected by every REST endpoint (03 section 6). User tokens at the gateway: M4.</summary>
[Collection(SqlCollection.Name)]
public sealed class DeviceTokenTests(SqlServerFixture sql) : EndpointSuiteBase(sql)
{
    private string DeviceToken(string key, string audience)
    {
        var now = App.Clock.GetUtcNow().UtcDateTime;
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "monitor-cloud",
            Audience = audience,
            Subject = new ClaimsIdentity([new Claim("sub", Guid.CreateVersion7().ToString()), new Claim("typ", "device"), new Claim("tid", World.A.Id.ToString())]),
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(60),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256),
        });
    }

    public static TheoryData<string> Variants => new() { "device-key", "user-key" };

    [Theory]
    [MemberData(nameof(Variants))]
    public async Task Device_tokens_are_rejected_by_every_rest_endpoint(string variant)
    {
        var token = variant == "device-key"
            ? DeviceToken(TestApp.DeviceSigningKey, "monitor-agent-gateway")
            : DeviceToken(TestApp.UserSigningKey, "monitor-cloud-portal");
        using var client = App.ClientWithToken(token);
        var failures = new List<string>();

        // Agent endpoints are the ones that take device tokens (credential rotation).
        foreach (var endpoint in Endpoints.Where(e => !EndpointCatalog.AnonymousAllowList.Contains(e.Key) && !e.IsAgent))
        {
            using var request = Request(endpoint, Guid.CreateVersion7());
            using var response = await client.SendAsync(request);
            if (response.StatusCode != HttpStatusCode.Unauthorized)
                failures.Add($"{endpoint} -> {(int)response.StatusCode}");
        }

        failures.ShouldBeEmpty();
    }
}
