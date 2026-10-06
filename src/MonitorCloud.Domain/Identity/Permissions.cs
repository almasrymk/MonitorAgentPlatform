namespace MonitorCloud.Domain.Identity;

/// <summary>Permission constants (03 section 2). The role-to-permission map is code, not data.</summary>
public static class Permissions
{
    public const string PlatformDashboardRead = "platform.dashboard.read";
    public const string PlatformTenantsRead = "platform.tenants.read";
    public const string PlatformTenantsManage = "platform.tenants.manage";
    public const string PlatformWorkspaceOpen = "platform.workspace.open";
    public const string PlatformPlansRead = "platform.plans.read";
    public const string PlatformUsersManage = "platform.users.manage";
    public const string PlatformSettingsManage = "platform.settings.manage";
    public const string PlatformAuditRead = "platform.audit.read";
    public const string PlatformLicensingSync = "platform.licensing.sync";
    public const string DashboardRead = "dashboard.read";
    public const string LocationsRead = "locations.read";
    public const string LocationsManage = "locations.manage";
    public const string DevicesRead = "devices.read";
    public const string DevicesManage = "devices.manage";
    public const string DevicesEnroll = "devices.enroll";
    public const string MonitorPointsManage = "monitorpoints.manage";
    public const string DevicesConfigure = "devices.configure";
    public const string AlertsRead = "alerts.read";
    public const string AlertsManage = "alerts.manage";
    public const string NotificationsRead = "notifications.read";
    public const string ReportsRead = "reports.read";
    public const string ReportsGenerate = "reports.generate";
    public const string SubscriptionRead = "subscription.read";
    public const string UsersManage = "users.manage";
    public const string SettingsManage = "settings.manage";
    public const string ArchiveRead = "archive.read";
    public const string ArchiveManage = "archive.manage";
    public const string ArchiveInternal = "archive.internal";
    public const string AuditRead = "audit.read";
}
