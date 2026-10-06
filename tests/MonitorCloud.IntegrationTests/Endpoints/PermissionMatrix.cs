namespace MonitorCloud.IntegrationTests.Endpoints;

/// <summary>The role-to-permission matrix of 03 section 2, written as test data (independent of the production map).</summary>
public static class PermissionMatrix
{
    public const string PlatformAdmin = "PlatformAdmin";
    public const string PlatformSupport = "PlatformSupport";
    public const string Administrator = "Administrator";
    public const string ITManager = "ITManager";
    public const string Technician = "Technician";
    public const string ReportViewer = "ReportViewer";

    public static readonly string[] Roles = [PlatformAdmin, PlatformSupport, Administrator, ITManager, Technician, ReportViewer];

    private static readonly Dictionary<string, string> Rows = new(StringComparer.Ordinal)
    {
        // permission                   PA PS Ad IT Te RV
        ["platform.dashboard.read"] = "xx....",
        ["platform.tenants.read"] = "xx....",
        ["platform.tenants.manage"] = "x.....",
        ["platform.workspace.open"] = "xx....",
        ["platform.plans.read"] = "xx....",
        ["platform.users.manage"] = "x.....",
        ["platform.settings.manage"] = "x.....",
        ["platform.audit.read"] = "xx....",
        ["platform.licensing.sync"] = "x.....",
        ["dashboard.read"] = "xxxxxx",
        ["locations.read"] = "xxxxxx",
        ["locations.manage"] = "x.xx..",
        ["devices.read"] = "xxxxxx",
        ["devices.manage"] = "x.xx..",
        ["devices.enroll"] = "x.xx..",
        ["monitorpoints.manage"] = "x.xxx.",
        ["devices.configure"] = "x.xx..",
        ["alerts.read"] = "xxxxxx",
        ["alerts.manage"] = "xxxxx.",
        ["notifications.read"] = "xxxxxx",
        ["reports.read"] = "xxxxxx",
        ["reports.generate"] = "xxxx.x",
        ["subscription.read"] = "xxxx..",
        ["users.manage"] = "x.x...",
        ["settings.manage"] = "x.x...",
        ["archive.read"] = "xxxx..",
        ["archive.manage"] = "xxx...",
        ["archive.internal"] = "xx....",
        ["audit.read"] = "xxx...",
    };

    public static IReadOnlyCollection<string> Permissions => Rows.Keys;

    public static bool Allows(string role, string permission) =>
        Rows.TryGetValue(permission, out var row) && row[Array.IndexOf(Roles, role)] == 'x';

    public static IReadOnlyList<string> PermissionsOf(string role) =>
        Rows.Where(r => r.Value[Array.IndexOf(Roles, role)] == 'x').Select(r => r.Key).ToList();
}
