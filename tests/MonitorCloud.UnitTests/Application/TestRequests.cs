using MonitorCloud.Application.Abstractions.Authorization;
using MonitorCloud.Application.Abstractions.Caching;
using MonitorCloud.Application.Abstractions.Messaging;

namespace MonitorCloud.UnitTests.Application;

// Request types used only to exercise the pipeline behaviours.
[RequirePermission("devices.read")]
internal sealed record ReadDevicesQuery(string Filter) : IQuery<string>;

[RequirePermission("devices.manage")]
internal sealed record RenameDeviceCommand(string Name) : ICommand<string>;

[RequirePermission("devices.manage")]
internal sealed record RetireDeviceCommand : ICommand;

[PlatformOnly]
[RequirePermission("platform.tenants.read")]
internal sealed record ListTenantsQuery : IQuery<int>;

[PlatformOnly]
internal sealed record PlatformPingQuery : IQuery<int>;

[AllowDevice]
internal sealed record DeviceHeartbeatCommand : ICommand;

[AllowAnonymousRequest]
internal sealed record SignInCommand(string Email) : ICommand<string>;

[AllowAuthenticatedUser]
internal sealed record GetMeQuery : IQuery<string>;

[SystemOnly]
internal sealed record SystemJobCommand : ICommand;

internal sealed record UndeclaredQuery : IQuery<int>;

[RequirePermission("monitorpoints.manage")]
[RequiresFeature("monitoring.advanced")]
internal sealed record EditMonitorPointCommand : ICommand;

[RequirePermission("reports.read")]
[RequiresFeature("reports.basic")]
internal sealed record ReadReportQuery : IQuery<int>;

[RequirePermission("dashboard.read")]
internal sealed record DashboardQuery(string Key) : IQuery<int>, ICacheableQuery
{
    public string CacheKey => Key;
    public TimeSpan CacheDuration => TimeSpan.FromSeconds(10);
}
