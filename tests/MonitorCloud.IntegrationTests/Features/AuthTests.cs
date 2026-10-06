using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Identity;
using MonitorCloud.Domain.Audit;
using MonitorCloud.Domain.Identity;
using MonitorCloud.TestShared;
using MonitorCloud.TestShared.Builders;

namespace MonitorCloud.IntegrationTests.Features;

[Collection(SqlCollection.Name)]
public sealed class AuthTests(SqlServerFixture sql) : FeatureTestBase(sql)
{
    private async Task<AuthResultDto> LoginAsync(string email, string password = TestWorld.Password)
    {
        using var client = App.CreateClient();
        return await (await client.PostJsonAsync("/api/v1/auth/login", Credentials(email, password))).ShouldBeOkAsync<AuthResultDto>();
    }

    [Fact]
    public async Task Tenant_user_signs_in_and_gets_the_profile_with_permissions()
    {
        var result = await LoginAsync(World.A.ReportViewer.Email);

        result.AccessToken.ShouldNotBeNullOrWhiteSpace();
        result.RefreshToken.ShouldNotBeNullOrWhiteSpace();
        result.ExpiresAt.ShouldBe(App.Clock.GetUtcNow().AddMinutes(15));
        result.User.TenantId.ShouldBe(World.A.Id);
        result.User.TenantName.ShouldBe(World.A.Tenant.Name);
        result.User.Role.ShouldBe(Roles.ReportViewer);
        result.User.Permissions.ShouldBe(Roles.PermissionsOf(Roles.ReportViewer).Order(StringComparer.Ordinal).ToList());
    }

    [Fact]
    public async Task Email_is_case_insensitive_and_sign_in_is_audited()
    {
        await LoginAsync(World.PlatformAdmin.Email.ToUpperInvariant());

        var audit = await App.InDbAsync(db => db.Set<AuditRecord>().Where(a => a.Action == "auth.login.succeeded").ToListAsync());
        audit.ShouldHaveSingleItem().EntityId.ShouldBe(World.PlatformAdmin.Id.ToString());
    }

    [Fact]
    public async Task Access_token_works_for_me_and_contains_the_claims_of_03()
    {
        var result = await LoginAsync(World.A.RestrictedManager.Email);
        using var client = App.ClientWithToken(result.AccessToken);

        var me = await (await client.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative))).ShouldBeOkAsync<UserProfileDto>();

        me.Id.ShouldBe(World.A.RestrictedManager.Id);
        me.LocationIds.ShouldBe([World.A.Location1.Id]);
        var claims = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(result.AccessToken).Claims.ToList();
        claims.Single(c => c.Type == "typ").Value.ShouldBe("user");
        claims.Single(c => c.Type == "tid").Value.ShouldBe(World.A.Id.ToString());
        claims.Single(c => c.Type == "loc").Value.ShouldBe(World.A.Location1.Id.ToString());
        claims.Where(c => c.Type == "perm").Select(c => c.Value).ShouldContain("devices.manage");
        claims.ShouldContain(c => c.Type == "jti");
    }

    [Fact]
    public async Task Unknown_email_and_wrong_password_get_the_same_answer()
    {
        using var client = App.CreateClient();

        var unknown = await (await client.PostJsonAsync("/api/v1/auth/login", Credentials("nobody@alpha.test"))).ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "AUTH_INVALID_CREDENTIALS");
        var wrong = await (await client.PostJsonAsync("/api/v1/auth/login", Credentials(World.A.Administrator.Email, "Wrong-Pass#1"))).ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "AUTH_INVALID_CREDENTIALS");

        unknown.GetProperty("detail").GetString().ShouldBe(wrong.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Five_failures_lock_the_account_for_15_minutes()
    {
        using var client = App.CreateClient();
        var email = World.A.Technician.Email;

        for (var i = 0; i < 4; i++)
            await (await client.PostJsonAsync("/api/v1/auth/login", Credentials(email, "Wrong-Pass#1"))).ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "AUTH_INVALID_CREDENTIALS");
        var locked = await (await client.PostJsonAsync("/api/v1/auth/login", Credentials(email, "Wrong-Pass#1"))).ShouldBeProblemAsync(HttpStatusCode.Locked, "AUTH_LOCKED");
        locked.GetProperty("retryAfterSeconds").GetInt32().ShouldBe(900);

        // Even the right password is refused while locked.
        await (await client.PostJsonAsync("/api/v1/auth/login", Credentials(email))).ShouldBeProblemAsync(HttpStatusCode.Locked, "AUTH_LOCKED");

        App.Clock.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(1));
        (await client.PostJsonAsync("/api/v1/auth/login", Credentials(email))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Refresh_rotates_the_token()
    {
        var first = await LoginAsync(World.A.Administrator.Email);
        using var client = App.CreateClient();

        var second = await (await client.PostJsonAsync("/api/v1/auth/refresh", new { refreshToken = first.RefreshToken })).ShouldBeOkAsync<AuthResultDto>();

        second.RefreshToken.ShouldNotBe(first.RefreshToken);
        (await client.PostJsonAsync("/api/v1/auth/refresh", new { refreshToken = second.RefreshToken })).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Reusing_a_rotated_refresh_token_revokes_the_whole_family_and_is_audited()
    {
        var first = await LoginAsync(World.A.Administrator.Email);
        using var client = App.CreateClient();
        var second = await (await client.PostJsonAsync("/api/v1/auth/refresh", new { refreshToken = first.RefreshToken })).ShouldBeOkAsync<AuthResultDto>();

        await (await client.PostJsonAsync("/api/v1/auth/refresh", new { refreshToken = first.RefreshToken })).ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "AUTH_REFRESH_INVALID");
        // The legitimate newer token is revoked too.
        await (await client.PostJsonAsync("/api/v1/auth/refresh", new { refreshToken = second.RefreshToken })).ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "AUTH_REFRESH_INVALID");

        var audit = await App.InDbAsync(db => db.Set<AuditRecord>().CountAsync(a => a.Action == "auth.refresh.reuse"));
        audit.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Refresh_token_expires_after_14_days()
    {
        var first = await LoginAsync(World.A.Administrator.Email);
        App.Clock.Advance(TimeSpan.FromDays(14) + TimeSpan.FromMinutes(1));
        using var client = App.CreateClient();

        await (await client.PostJsonAsync("/api/v1/auth/refresh", new { refreshToken = first.RefreshToken })).ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "AUTH_REFRESH_INVALID");
    }

    [Fact]
    public async Task Access_token_expires_after_15_minutes()
    {
        var result = await LoginAsync(World.A.Administrator.Email);
        using var client = App.ClientWithToken(result.AccessToken);
        App.Clock.Advance(TimeSpan.FromMinutes(16));

        (await client.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        var result = await LoginAsync(World.A.Administrator.Email);
        using var client = App.CreateClient();

        (await client.PostJsonAsync("/api/v1/auth/logout", new { refreshToken = result.RefreshToken })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await (await client.PostJsonAsync("/api/v1/auth/refresh", new { refreshToken = result.RefreshToken })).ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "AUTH_REFRESH_INVALID");
        (await client.PostJsonAsync("/api/v1/auth/logout", new { refreshToken = "unknown-token" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Suspended_customer_cannot_sign_in_or_refresh()
    {
        var session = await LoginAsync(World.A.Administrator.Email);
        using var admin = App.ClientFor(World.PlatformAdmin);
        (await admin.PostJsonAsync($"/api/v1/platform/tenants/{World.A.Id}/suspend", new { reason = "Unpaid invoice" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var client = App.CreateClient();

        await (await client.PostJsonAsync("/api/v1/auth/login", Credentials(World.A.Administrator.Email))).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "AUTH_TENANT_SUSPENDED");
        await (await client.PostJsonAsync("/api/v1/auth/refresh", new { refreshToken = session.RefreshToken })).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "AUTH_TENANT_SUSPENDED");
        (await client.PostJsonAsync("/api/v1/auth/login", Credentials(World.B.Administrator.Email))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Inactive_users_cannot_sign_in()
    {
        using var admin = App.ClientFor(World.A.Administrator);
        (await admin.PostAsync(new Uri($"/api/v1/users/{World.A.Technician.Id}/deactivate", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var client = App.CreateClient();

        await (await client.PostJsonAsync("/api/v1/auth/login", Credentials(World.A.Technician.Email))).ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "AUTH_INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task Changing_the_password_requires_the_current_one_and_the_policy()
    {
        using var client = App.ClientFor(World.A.Administrator);

        await (await client.PutJsonAsync("/api/v1/auth/me/password", new { currentPassword = "Wrong-Pass#1", newPassword = "New-Pass#2026x" })).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "AUTH_PASSWORD_INCORRECT");
        await (await client.PutJsonAsync("/api/v1/auth/me/password", new { currentPassword = TestWorld.Password, newPassword = "short" })).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "AUTH_PASSWORD_WEAK");
        (await client.PutJsonAsync("/api/v1/auth/me/password", new { currentPassword = TestWorld.Password, newPassword = "New-Pass#2026x" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await LoginAsync(World.A.Administrator.Email, "New-Pass#2026x");
    }

    [Fact]
    public async Task Language_preference_is_saved_and_returned()
    {
        using var client = App.ClientFor(World.A.Technician);

        (await client.PutJsonAsync("/api/v1/auth/me/language", new { language = "ar" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await (await client.PutJsonAsync("/api/v1/auth/me/language", new { language = "fr" })).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");

        (await LoginAsync(World.A.Technician.Email)).User.Language.ShouldBe("ar");
    }

    [Fact]
    public async Task Tampered_tokens_are_rejected()
    {
        var result = await LoginAsync(World.A.ReportViewer.Email);
        var parts = result.AccessToken.Split('.');
        var payload = JsonDocument.Parse(Microsoft.IdentityModel.Tokens.Base64UrlEncoder.Decode(parts[1])).RootElement.GetRawText()
            .Replace("ReportViewer", "Administrator", StringComparison.Ordinal);
        var forged = $"{parts[0]}.{Microsoft.IdentityModel.Tokens.Base64UrlEncoder.Encode(payload)}.{parts[2]}";
        var unsigned = $"{Microsoft.IdentityModel.Tokens.Base64UrlEncoder.Encode("{\"alg\":\"none\",\"typ\":\"JWT\"}")}.{parts[1]}.";

        foreach (var token in new[] { forged, unsigned })
        {
            using var client = App.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            (await client.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
    }

    [Fact]
    public async Task Secrets_never_appear_in_audit_details()
    {
        var result = await LoginAsync(World.A.Administrator.Email);
        using var client = App.CreateClient();
        await client.PostJsonAsync("/api/v1/auth/refresh", new { refreshToken = result.RefreshToken });

        var details = await App.InDbAsync(db => db.Set<AuditRecord>().Select(a => a.Details ?? string.Empty).ToListAsync());
        details.ShouldAllBe(d => !d.Contains(result.RefreshToken) && !d.Contains(TestWorld.Password));
        var tokens = await App.InDbAsync(db => db.Set<RefreshToken>().Select(t => t.TokenHash).ToListAsync());
        tokens.ShouldNotContain(result.RefreshToken);
    }
}
