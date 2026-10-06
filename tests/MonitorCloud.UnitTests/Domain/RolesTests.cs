using MonitorCloud.Domain.Identity;

namespace MonitorCloud.UnitTests.Domain;

public sealed class RolesTests
{
    [Fact]
    public void Platform_admin_has_every_permission() =>
        Roles.PermissionsOf(Roles.PlatformAdmin).Count.ShouldBe(29);

    [Fact]
    public void Report_viewer_is_read_only_plus_report_generation()
    {
        var permissions = Roles.PermissionsOf(Roles.ReportViewer);

        permissions.ShouldContain(Permissions.ReportsGenerate);
        permissions.ShouldNotContain(Permissions.UsersManage);
        permissions.ShouldNotContain(Permissions.SettingsManage);
        permissions.ShouldNotContain(Permissions.LocationsManage);
    }

    [Theory]
    [InlineData(Roles.PlatformAdmin, "Platform Admin", "Full Access")]
    [InlineData(Roles.Administrator, "Administrator", "Full Access")]
    [InlineData(Roles.ITManager, "IT Manager", "Manage Devices")]
    [InlineData(Roles.Technician, "Technician", "Limited Access")]
    [InlineData(Roles.ReportViewer, "Report Viewer", "View Reports")]
    public void Display_names_and_summaries_follow_the_designs(string role, string name, string summary)
    {
        Roles.DisplayName(role).ShouldBe(name);
        Roles.PermissionSummary(role).ShouldBe(summary);
    }

    [Fact]
    public void Unknown_roles_have_nothing()
    {
        Roles.IsKnown("Root").ShouldBeFalse();
        Roles.PermissionsOf("Root").ShouldBeEmpty();
        Roles.Grants("Root", Permissions.DevicesRead).ShouldBeFalse();
        Roles.DisplayName("Root").ShouldBe("Root");
        Roles.PermissionSummary("Root").ShouldBe(string.Empty);
    }

    [Fact]
    public void Role_groups_are_disjoint()
    {
        Roles.Platform.Intersect(Roles.Tenant).ShouldBeEmpty();
        Roles.All.Count.ShouldBe(6);
        Roles.All.ShouldAllBe(r => Roles.IsKnown(r));
    }
}
