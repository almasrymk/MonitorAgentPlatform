namespace MonitorCloud.IntegrationTests.Endpoints;

/// <summary>
/// What each endpoint requires, written as test data from 06-api.md (independent of the request attributes).
/// <c>"anonymous"</c> = no token; <c>"*"</c> = any signed-in user. A new endpoint without a row fails the suites.
/// </summary>
public static class EndpointPermissions
{
    public const string Anonymous = "anonymous";
    public const string AnyUser = "*";

    /// <summary>Device token only (agent endpoints); user tokens get 401 from the Device scheme.</summary>
    public const string DeviceOnly = "device";

    public static readonly IReadOnlyDictionary<string, string> Rows = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["POST /api/v1/auth/login"] = Anonymous,
        ["POST /api/v1/auth/refresh"] = Anonymous,
        ["POST /api/v1/auth/logout"] = Anonymous,
        ["POST /api/v1/auth/invitations/accept"] = Anonymous,
        ["GET /api/v1/auth/me"] = AnyUser,
        ["PUT /api/v1/auth/me/password"] = AnyUser,
        ["PUT /api/v1/auth/me/language"] = AnyUser,

        ["GET /api/v1/platform/tenants"] = "platform.tenants.read",
        ["GET /api/v1/platform/tenants/summary"] = "platform.tenants.read",
        ["POST /api/v1/platform/tenants"] = "platform.tenants.manage",
        ["GET /api/v1/platform/tenants/{id:guid}"] = "platform.tenants.read",
        ["PUT /api/v1/platform/tenants/{id:guid}"] = "platform.tenants.manage",
        ["POST /api/v1/platform/tenants/{id:guid}/suspend"] = "platform.tenants.manage",
        ["POST /api/v1/platform/tenants/{id:guid}/resume"] = "platform.tenants.manage",
        ["POST /api/v1/platform/tenants/{id:guid}/archive"] = "platform.tenants.manage",
        ["POST /api/v1/platform/tenants/{id:guid}/workspace-sessions"] = "platform.workspace.open",
        ["DELETE /api/v1/platform/tenants/{id:guid}/workspace-sessions/current"] = "platform.workspace.open",
        ["GET /api/v1/platform/users"] = "platform.users.manage",
        ["POST /api/v1/platform/users"] = "platform.users.manage",
        ["PUT /api/v1/platform/users/{id:guid}"] = "platform.users.manage",
        ["POST /api/v1/platform/users/{id:guid}/activate"] = "platform.users.manage",
        ["POST /api/v1/platform/users/{id:guid}/deactivate"] = "platform.users.manage",
        ["POST /api/v1/platform/users/{id:guid}/reset-password"] = "platform.users.manage",
        ["GET /api/v1/platform/audit"] = "platform.audit.read",
        ["GET /api/v1/platform/plans"] = "platform.plans.read",
        ["GET /api/v1/platform/licensing/status"] = "platform.dashboard.read",
        ["POST /api/v1/platform/licensing/sync"] = "platform.licensing.sync",
        ["GET /api/v1/subscription"] = "subscription.read",

        ["GET /api/v1/locations"] = "locations.read",
        ["POST /api/v1/locations"] = "locations.manage",
        ["GET /api/v1/locations/{id:guid}"] = "locations.read",
        ["PUT /api/v1/locations/{id:guid}"] = "locations.manage",
        ["DELETE /api/v1/locations/{id:guid}"] = "locations.manage",

        ["GET /api/v1/users"] = "users.manage",
        ["POST /api/v1/users"] = "users.manage",
        ["PUT /api/v1/users/{id:guid}"] = "users.manage",
        ["POST /api/v1/users/{id:guid}/activate"] = "users.manage",
        ["POST /api/v1/users/{id:guid}/deactivate"] = "users.manage",
        ["POST /api/v1/users/{id:guid}/resend-invitation"] = "users.manage",
        ["GET /api/v1/roles"] = "users.manage",
        ["GET /api/v1/audit"] = "audit.read",

        ["POST /api/agent/v1/enroll"] = Anonymous,
        ["POST /api/agent/v1/token"] = Anonymous,
        ["POST /api/agent/v1/credential/rotate"] = DeviceOnly,

        ["GET /api/v1/devices"] = "devices.read",
        ["GET /api/v1/devices/summary"] = "devices.read",
        ["GET /api/v1/devices/{id:guid}"] = "devices.read",
        ["PUT /api/v1/devices/{id:guid}"] = "devices.manage",
        ["POST /api/v1/devices/{id:guid}/retire"] = "devices.manage",
        ["POST /api/v1/devices/{id:guid}/unlicense"] = "devices.manage",
        ["GET /api/v1/devices/{id:guid}/overview"] = "devices.read",
        ["GET /api/v1/devices/{id:guid}/metrics"] = "devices.read",
        ["GET /api/v1/devices/{id:guid}/disks"] = "devices.read",
        ["GET /api/v1/devices/{id:guid}/inventory/{kind}"] = "devices.read",
        ["POST /api/v1/devices/{id:guid}/live-sessions"] = "devices.read",
        ["GET /api/v1/dashboard"] = "dashboard.read",
        ["GET /api/v1/locations/{id:guid}/dashboard"] = "dashboard.read",
        ["GET /api/v1/platform/dashboard"] = "platform.dashboard.read",
        ["POST /api/v1/locations/{id:guid}/enrollment-codes"] = "devices.enroll",
        ["GET /api/v1/locations/{id:guid}/enrollment-codes"] = "devices.enroll",
        ["DELETE /api/v1/locations/{id:guid}/enrollment-codes/{codeId:guid}"] = "devices.enroll",

        ["GET /api/v1/alerts"] = "alerts.read",
        ["GET /api/v1/alerts/{id:guid}"] = "alerts.read",
        ["POST /api/v1/alerts/{id:guid}/acknowledge"] = "alerts.manage",
        ["POST /api/v1/alerts/{id:guid}/resolve"] = "alerts.manage",
        ["GET /api/v1/devices/{id:guid}/alerts"] = "alerts.read",
        ["GET /api/v1/devices/{id:guid}/monitor-points"] = "devices.read",
        ["GET /api/v1/notifications"] = "notifications.read",
        ["GET /api/v1/notifications/unread-count"] = "notifications.read",
        ["POST /api/v1/notifications/read"] = "notifications.read",
        ["GET /api/v1/platform/alerts"] = "platform.dashboard.read",
        ["GET /api/v1/platform/notifications"] = "platform.dashboard.read",
        ["GET /api/v1/platform/notifications/unread-count"] = "platform.dashboard.read",
        ["POST /api/v1/platform/notifications/read"] = "platform.dashboard.read",
        ["GET /api/v1/settings/general"] = "settings.manage",
        ["PUT /api/v1/settings/general"] = "settings.manage",
        ["GET /api/v1/settings/alerts"] = "settings.manage",
        ["PUT /api/v1/settings/alerts"] = "settings.manage",
        ["GET /api/v1/settings/alerts/recipients"] = "settings.manage",
        ["POST /api/v1/settings/alerts/recipients"] = "settings.manage",
        ["PUT /api/v1/settings/alerts/recipients/{id:guid}"] = "settings.manage",
        ["DELETE /api/v1/settings/alerts/recipients/{id:guid}"] = "settings.manage",
    };
}
