using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using MonitorCloud.Domain.Identity;
using MonitorCloud.Infrastructure.Identity;
using MonitorCloud.Infrastructure.Options;
using MonitorCloud.TestShared;

namespace MonitorCloud.UnitTests.Infrastructure;

public sealed class IdentityServicesTests
{
    private static readonly TokenService Tokens = new(Options.Create(new JwtOptions
    {
        SigningKey = "TEST-ONLY-user-signing-key-0123456789abcdef",
        DeviceSigningKey = "TEST-ONLY-device-signing-key-0123456789abcdef",
    }));

    [Fact]
    public void Access_token_has_the_claims_of_03()
    {
        var location = Guid.CreateVersion7();
        var user = User.CreateTenantUser(Guid.CreateVersion7(), "it@alpha.test", "IT", Roles.ITManager, [location], "hash", TestClock.DefaultStart);

        var token = Tokens.CreateAccessToken(user, TestClock.DefaultStart);
        var jwt = new JsonWebToken(token.Token);

        token.ExpiresAt.ShouldBe(TestClock.DefaultStart.AddMinutes(15));
        jwt.Issuer.ShouldBe("monitor-cloud");
        jwt.Audiences.ShouldBe(["monitor-cloud-portal"]);
        jwt.Alg.ShouldBe("HS256");
        jwt.GetClaim("sub").Value.ShouldBe(user.Id.ToString());
        jwt.GetClaim("typ").Value.ShouldBe("user");
        jwt.GetClaim("tid").Value.ShouldBe(user.TenantId.ToString());
        jwt.GetClaim("loc").Value.ShouldBe(location.ToString());
        jwt.GetClaim("lang").Value.ShouldBe("en");
        jwt.Claims.Where(c => c.Type == "perm").Select(c => c.Value).ShouldBe(Roles.PermissionsOf(Roles.ITManager), ignoreOrder: true);
    }

    [Fact]
    public void Platform_token_has_no_tenant_claim()
    {
        var user = User.CreatePlatformUser("a@monitor.local", "A", Roles.PlatformAdmin, "hash", TestClock.DefaultStart);

        var jwt = new JsonWebToken(Tokens.CreateAccessToken(user, TestClock.DefaultStart).Token);

        jwt.TryGetClaim("tid", out _).ShouldBeFalse();
        jwt.TryGetClaim("loc", out _).ShouldBeFalse();
    }

    [Fact]
    public void Secrets_are_random_and_hashed_per_purpose()
    {
        var refresh = Tokens.NewRefreshToken();
        var other = Tokens.NewRefreshToken();

        refresh.Token.ShouldNotBe(other.Token);
        refresh.Hash.ShouldBe(Tokens.HashRefreshToken(refresh.Token));
        refresh.Hash.ShouldNotBe(refresh.Token);
        Tokens.HashInvitationToken(refresh.Token).ShouldNotBe(refresh.Hash);
        Tokens.NewInvitationToken().Hash.Length.ShouldBe(64);
    }

    [Fact]
    public void Password_hasher_verifies_and_rejects()
    {
        var hasher = new PasswordHasherAdapter();
        var hash = hasher.Hash("Correct-Horse#9");

        hasher.Verify(hash, "Correct-Horse#9").ShouldBeTrue();
        hasher.Verify(hash, "Wrong-Horse#9").ShouldBeFalse();
        hasher.Verify("not-base64!", "x").ShouldBeFalse();
    }
}
