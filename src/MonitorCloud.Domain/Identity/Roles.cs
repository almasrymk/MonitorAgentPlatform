using System.Collections.Frozen;
using static MonitorCloud.Domain.Identity.Permissions;

namespace MonitorCloud.Domain.Identity;

/// <summary>The six roles of 03 section 2 and their permissions.</summary>
public static class Roles
{
    public const string PlatformAdmin = "PlatformAdmin";
    public const string PlatformSupport = "PlatformSupport";
    public const string Administrator = "Administrator";
    public const string ITManager = "ITManager";
    public const string Technician = "Technician";
    public const string ReportViewer = "ReportViewer";

    public static readonly IReadOnlyList<string> Platform = [PlatformAdmin, PlatformSupport];
    public static readonly IReadOnlyList<string> Tenant = [Administrator, ITManager, Technician, ReportViewer];
    public static readonly IReadOnlyList<string> All = [.. Platform, .. Tenant];

    private static readonly string[] SharedRead = [DashboardRead, LocationsRead, DevicesRead, AlertsRead, NotificationsRead, ReportsRead];

    private static readonly FrozenDictionary<string, FrozenSet<string>> Map = new Dictionary<string, FrozenSet<string>>(StringComparer.Ordinal)
    {
        [PlatformAdmin] = Set([
            PlatformDashboardRead, PlatformTenantsRead, PlatformTenantsManage, PlatformWorkspaceOpen, PlatformPlansRead,
            PlatformUsersManage, PlatformSettingsManage, PlatformAuditRead, PlatformLicensingSync,
            DashboardRead, LocationsRead, LocationsManage, DevicesRead, DevicesManage, DevicesEnroll, MonitorPointsManage,
            DevicesConfigure, AlertsRead, AlertsManage, NotificationsRead, ReportsRead, ReportsGenerate, SubscriptionRead,
            UsersManage, SettingsManage, ArchiveRead, ArchiveManage, ArchiveInternal, AuditRead,
        ]),
        [PlatformSupport] = Set([
            PlatformDashboardRead, PlatformTenantsRead, PlatformWorkspaceOpen, PlatformPlansRead, PlatformAuditRead,
            .. SharedRead, AlertsManage, ReportsGenerate, SubscriptionRead, ArchiveRead, ArchiveManage, ArchiveInternal, AuditRead,
        ]),
        [Administrator] = Set([
            .. SharedRead, LocationsManage, DevicesManage, DevicesEnroll, MonitorPointsManage, DevicesConfigure, AlertsManage,
            ReportsGenerate, SubscriptionRead, UsersManage, SettingsManage, ArchiveRead, ArchiveManage, AuditRead,
        ]),
        [ITManager] = Set([
            .. SharedRead, LocationsManage, DevicesManage, DevicesEnroll, MonitorPointsManage, DevicesConfigure, AlertsManage,
            ReportsGenerate, SubscriptionRead, ArchiveRead,
        ]),
        [Technician] = Set([ .. SharedRead, MonitorPointsManage, AlertsManage ]),
        [ReportViewer] = Set([ .. SharedRead, ReportsGenerate ]),
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static FrozenSet<string> Set(string[] permissions) => permissions.ToFrozenSet(StringComparer.Ordinal);

    public static bool IsKnown(string? role) => role is not null && Map.ContainsKey(role);

    public static bool IsPlatformRole(string? role) => role is PlatformAdmin or PlatformSupport;

    public static bool IsTenantRole(string? role) => role is Administrator or ITManager or Technician or ReportViewer;

    public static IReadOnlySet<string> PermissionsOf(string role) =>
        Map.TryGetValue(role, out var permissions) ? permissions : FrozenSet<string>.Empty;

    public static bool Grants(string role, string permission) => PermissionsOf(role).Contains(permission);

    /// <summary>Display names used by the designs.</summary>
    public static string DisplayName(string role) => role switch
    {
        PlatformAdmin => "Platform Admin",
        PlatformSupport => "Platform Support",
        Administrator => "Administrator",
        ITManager => "IT Manager",
        Technician => "Technician",
        ReportViewer => "Report Viewer",
        _ => role,
    };

    /// <summary>The "Permissions" column of the users table.</summary>
    public static string PermissionSummary(string role) => role switch
    {
        PlatformAdmin or Administrator => "Full Access",
        ITManager => "Manage Devices",
        Technician or PlatformSupport => "Limited Access",
        ReportViewer => "View Reports",
        _ => string.Empty,
    };
}
