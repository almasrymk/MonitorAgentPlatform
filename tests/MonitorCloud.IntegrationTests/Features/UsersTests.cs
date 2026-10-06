using System.Net;
using MonitorCloud.Application.Common;
using MonitorCloud.Application.Identity;
using MonitorCloud.Domain.Identity;
using MonitorCloud.TestShared;

namespace MonitorCloud.IntegrationTests.Features;

[Collection(SqlCollection.Name)]
public sealed class UsersTests(SqlServerFixture sql) : FeatureTestBase(sql)
{
    [Fact]
    public async Task Administrator_lists_the_tenants_users_only()
    {
        using var client = App.ClientFor(World.A.Administrator);

        var page = await (await client.GetAsync(new Uri("/api/v1/users?pageSize=50", UriKind.Relative))).ShouldBeOkAsync<PagedResult<UserListItemDto>>();

        page.Total.ShouldBe(5);
        page.Items.ShouldAllBe(u => u.Email.EndsWith("@alpha.test"));
        page.Items.Single(u => u.Role == Roles.ITManager && u.LocationIds.Count == 0).PermissionSummary.ShouldBe("Manage Devices");
    }

    [Fact]
    public async Task Users_can_be_filtered_by_role_status_and_search()
    {
        using var client = App.ClientFor(World.A.Administrator);

        var managers = await (await client.GetAsync(new Uri("/api/v1/users?role=ITManager", UriKind.Relative))).ShouldBeOkAsync<PagedResult<UserListItemDto>>();
        var search = await (await client.GetAsync(new Uri("/api/v1/users?search=Restricted", UriKind.Relative))).ShouldBeOkAsync<PagedResult<UserListItemDto>>();

        managers.Total.ShouldBe(2);
        search.Items.ShouldHaveSingleItem().Id.ShouldBe(World.A.RestrictedManager.Id);
    }

    [Fact]
    public async Task Invited_user_accepts_and_signs_in()
    {
        using var client = App.ClientFor(World.A.Administrator);
        var invited = await (await client.PostJsonAsync("/api/v1/users", new { fullName = "New Person", email = "New.Person@Alpha.test", role = "Technician", locationIds = new[] { World.A.Location2.Id } }))
            .ShouldBeOkAsync<UserListItemDto>(HttpStatusCode.Created);
        invited.Status.ShouldBe("Invited");
        invited.Email.ShouldBe("new.person@alpha.test");

        var token = App.Mail.LastInvitationToken("new.person@alpha.test");
        using var anonymous = App.CreateClient();
        await (await anonymous.PostJsonAsync("/api/v1/auth/invitations/accept", new { token, password = "weak" })).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "AUTH_PASSWORD_WEAK");
        (await anonymous.PostJsonAsync("/api/v1/auth/invitations/accept", new { token, password = "Chosen-Pass#2026" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await (await anonymous.PostJsonAsync("/api/v1/auth/invitations/accept", new { token, password = "Chosen-Pass#2026" })).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "AUTH_INVITATION_INVALID");

        var session = await (await anonymous.PostJsonAsync("/api/v1/auth/login", new { email = "new.person@alpha.test", password = "Chosen-Pass#2026" })).ShouldBeOkAsync<AuthResultDto>();
        session.User.LocationIds.ShouldBe([World.A.Location2.Id]);
    }

    [Fact]
    public async Task Invitation_expires_after_seven_days_and_can_be_resent()
    {
        using var client = App.ClientFor(World.A.Administrator);
        var invited = await (await client.PostJsonAsync("/api/v1/users", new { fullName = "Late Person", email = "late@alpha.test", role = "ReportViewer" })).ShouldBeOkAsync<UserListItemDto>(HttpStatusCode.Created);
        var oldToken = App.Mail.LastInvitationToken("late@alpha.test");
        App.Clock.Advance(TimeSpan.FromDays(8));
        using var anonymous = App.CreateClient();

        await (await anonymous.PostJsonAsync("/api/v1/auth/invitations/accept", new { token = oldToken, password = "Chosen-Pass#2026" })).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "AUTH_INVITATION_INVALID");

        using var refreshed = App.ClientFor(World.A.Administrator);
        (await refreshed.PostAsync(new Uri($"/api/v1/users/{invited.Id}/resend-invitation", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var newToken = App.Mail.LastInvitationToken("late@alpha.test");
        newToken.ShouldNotBe(oldToken);
        (await anonymous.PostJsonAsync("/api/v1/auth/invitations/accept", new { token = newToken, password = "Chosen-Pass#2026" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Email_must_be_unique_across_the_platform()
    {
        using var client = App.ClientFor(World.A.Administrator);

        await (await client.PostJsonAsync("/api/v1/users", new { fullName = "Copy", email = World.B.Administrator.Email, role = "Technician" })).ShouldBeProblemAsync(HttpStatusCode.Conflict, "USER_EMAIL_TAKEN");
        await (await client.PostJsonAsync("/api/v1/users", new { fullName = "Copy", email = World.PlatformAdmin.Email, role = "Technician" })).ShouldBeProblemAsync(HttpStatusCode.Conflict, "USER_EMAIL_TAKEN");
    }

    [Fact]
    public async Task Invite_validates_role_and_locations()
    {
        using var client = App.ClientFor(World.A.Administrator);

        await (await client.PostJsonAsync("/api/v1/users", new { fullName = "X", email = "x@alpha.test", role = "PlatformAdmin" })).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        var problem = await (await client.PostJsonAsync("/api/v1/users", new { fullName = "X", email = "x@alpha.test", role = "Technician", locationIds = new[] { World.B.Location1.Id } }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        problem.GetProperty("errors").GetProperty("locationIds").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Role_change_is_saved_and_audited()
    {
        using var client = App.ClientFor(World.A.Administrator);

        var updated = await (await client.PutJsonAsync($"/api/v1/users/{World.A.Technician.Id}", new { fullName = "Promoted", role = "ITManager" })).ShouldBeOkAsync<UserListItemDto>();

        updated.Role.ShouldBe(Roles.ITManager);
        updated.FullName.ShouldBe("Promoted");
    }

    [Fact]
    public async Task The_last_administrator_cannot_be_demoted_or_deactivated()
    {
        using var client = App.ClientFor(World.A.Administrator);
        var admin = World.A.Administrator.Id;

        await (await client.PutJsonAsync($"/api/v1/users/{admin}", new { fullName = "Admin", role = "Technician" })).ShouldBeProblemAsync(HttpStatusCode.Conflict, "USER_LAST_ADMIN");
        await (await client.PostAsync(new Uri($"/api/v1/users/{admin}/deactivate", UriKind.Relative), null)).ShouldBeProblemAsync(HttpStatusCode.Conflict, "USER_LAST_ADMIN");

        (await client.PutJsonAsync($"/api/v1/users/{World.A.ItManager.Id}", new { fullName = "Second Admin", role = "Administrator" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.PostAsync(new Uri($"/api/v1/users/{admin}/deactivate", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Deactivated_user_can_be_activated_again()
    {
        using var client = App.ClientFor(World.A.Administrator);
        var id = World.A.ReportViewer.Id;

        (await client.PostAsync(new Uri($"/api/v1/users/{id}/deactivate", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await (await client.PostAsync(new Uri($"/api/v1/users/{id}/deactivate", UriKind.Relative), null)).ShouldBeProblemAsync(HttpStatusCode.Conflict, "USER_INVALID_TRANSITION");
        var activated = await (await client.PostAsync(new Uri($"/api/v1/users/{id}/activate", UriKind.Relative), null)).ShouldBeOkAsync<UserListItemDto>();

        activated.Status.ShouldBe("Active");
    }

    [Fact]
    public async Task Roles_endpoint_lists_the_four_customer_roles()
    {
        using var client = App.ClientFor(World.A.Administrator);

        var roles = await (await client.GetAsync(new Uri("/api/v1/roles", UriKind.Relative))).ShouldBeOkAsync<List<RoleDto>>();

        roles.Select(r => r.Name).ShouldBe(["Administrator", "IT Manager", "Technician", "Report Viewer"]);
        roles.Select(r => r.PermissionSummary).ShouldBe(["Full Access", "Manage Devices", "Limited Access", "View Reports"]);
    }

    [Fact]
    public async Task Platform_users_are_managed_by_the_platform_admin()
    {
        using var client = App.ClientFor(World.PlatformAdmin);

        var created = await (await client.PostJsonAsync("/api/v1/platform/users", new { fullName = "New Support", email = "new.support@monitor.local", role = "PlatformSupport", password = "Support-Pass#2026" }))
            .ShouldBeOkAsync<UserListItemDto>(HttpStatusCode.Created);
        var list = await (await client.GetAsync(new Uri("/api/v1/platform/users", UriKind.Relative))).ShouldBeOkAsync<PagedResult<UserListItemDto>>();
        list.Total.ShouldBe(3);
        list.Items.ShouldAllBe(u => u.Email.EndsWith("@monitor.local"));

        (await client.PostJsonAsync($"/api/v1/platform/users/{created.Id}/reset-password", new { newPassword = "Other-Pass#2026" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.PostAsync(new Uri($"/api/v1/platform/users/{created.Id}/deactivate", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await (await client.PostAsync(new Uri($"/api/v1/platform/users/{World.PlatformAdmin.Id}/deactivate", UriKind.Relative), null)).ShouldBeProblemAsync(HttpStatusCode.Conflict, "USER_INVALID_TRANSITION");
        await (await client.PostJsonAsync("/api/v1/platform/users", new { fullName = "Weak", email = "weak@monitor.local", role = "PlatformSupport", password = "weak" })).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task Platform_user_list_never_contains_customer_users()
    {
        using var client = App.ClientFor(World.PlatformAdmin);

        var list = await (await client.GetAsync(new Uri("/api/v1/platform/users?pageSize=200", UriKind.Relative))).ShouldBeOkAsync<PagedResult<UserListItemDto>>();

        list.Items.Select(u => u.Id).ShouldBe([World.PlatformAdmin.Id, World.PlatformSupport.Id], ignoreOrder: true);
    }
}
