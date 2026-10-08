namespace MonitorCloud.Application.Monitoring.Contracts;

public sealed record OpenAlertCounts(int Critical, int Warning);

/// <summary>Open alerts of a device, for <c>DeviceStates</c> (Devices module).</summary>
public interface IOpenAlertCounter
{
    Task<OpenAlertCounts> CountAsync(Guid deviceId, CancellationToken ct);
}

public sealed record AlertDay(DateOnly Day, int Critical, int Warning, int Info);

public sealed record AlertSeverityCounts(int Critical, int Warning, int Info, int Resolved);

public sealed record RecentAlert(Guid Id, DateTimeOffset At, string Severity, Guid TenantId, Guid LocationId, Guid DeviceId, string Title, string Message);

/// <summary>Alert figures for the dashboards (Tenancy module), in the caller's scope.</summary>
public interface IAlertDashboardReader
{
    /// <summary>Alerts opened per day in [from, to] (from <c>AlertDailyStats</c>); missing days are zero.</summary>
    Task<IReadOnlyList<AlertDay>> TrendAsync(Guid? locationId, DateOnly from, DateOnly to, CancellationToken ct);

    /// <summary>Alerts opened since <paramref name="since"/> by severity, and how many of them are resolved.</summary>
    Task<AlertSeverityCounts> BySeverityAsync(DateTimeOffset since, CancellationToken ct);

    /// <summary>The newest open alerts.</summary>
    Task<IReadOnlyList<RecentAlert>> RecentAsync(Guid? locationId, int take, CancellationToken ct);
}

public sealed record OfflineAlertSettings(string Severity, int DelayMinutes);

/// <summary>The tenant's <c>device-offline</c> alert settings (Settings > General).</summary>
public interface IMonitoringSettingsStore
{
    Task<OfflineAlertSettings> GetAsync(Guid tenantId, CancellationToken ct);

    /// <summary>Adds or changes the settings in the current unit of work (no save).</summary>
    Task SetAsync(Guid tenantId, string severity, int delayMinutes, CancellationToken ct);
}
