using MonitorCloud.SharedKernel;

namespace MonitorCloud.Domain.Monitoring;

/// <summary>Alerts opened per tenant, location and tenant-local day (02 section 6), for the trend charts.</summary>
public sealed class AlertDailyStat : Entity, ITenantOwned
{
    private AlertDailyStat()
    {
    }

    public Guid TenantId { get; private set; }
    public Guid LocationId { get; private set; }
    public DateOnly Day { get; private set; }
    public int Critical { get; private set; }
    public int Warning { get; private set; }
    public int Info { get; private set; }

    public static AlertDailyStat Create(Guid tenantId, Guid locationId, DateOnly day) =>
        new() { TenantId = Guard.NotEmpty(tenantId, nameof(TenantId)), LocationId = locationId, Day = day };

    public void Count(AlertSeverity severity, int by = 1)
    {
        switch (severity)
        {
            case AlertSeverity.Critical:
                Critical += by;
                break;
            case AlertSeverity.Warning:
                Warning += by;
                break;
            default:
                Info += by;
                break;
        }
    }
}

public enum PointStatus : byte
{
    Unknown = 1,
    Healthy = 2,
    Warning = 3,
    Critical = 4,
}

/// <summary>A monitor point of a device (02 section 6). From M6 they are imported from the agent's reports.</summary>
public sealed class MonitorPoint : AggregateRoot, ITenantOwned
{
    private MonitorPoint()
    {
        Key = string.Empty;
        DisplayName = string.Empty;
        Type = string.Empty;
        Target = string.Empty;
        AlertLevel = "Problem";
        Origin = "Agent";
    }

    public Guid TenantId { get; private set; }
    public Guid DeviceId { get; private set; }
    public string Key { get; private set; }
    public string DisplayName { get; private set; }
    public string Type { get; private set; }
    public string Target { get; private set; }
    public int IntervalSeconds { get; private set; }
    public string AlertLevel { get; private set; }
    public bool Enabled { get; private set; }
    public bool ShowInShortcut { get; private set; }
    public string Origin { get; private set; }
    public int SortOrder { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static MonitorPoint FromAgent(Guid tenantId, Guid deviceId, string key, string displayName, string type, string target, bool enabled, int intervalSeconds, int sortOrder) =>
        new()
        {
            TenantId = Guard.NotEmpty(tenantId, nameof(TenantId)),
            DeviceId = Guard.NotEmpty(deviceId, nameof(DeviceId)),
            Key = Guard.NotEmpty(key, nameof(Key), 64),
            DisplayName = Guard.NotEmpty(string.IsNullOrWhiteSpace(displayName) ? key : displayName, nameof(DisplayName), 200),
            Type = Guard.MaxLength(string.IsNullOrWhiteSpace(type) ? "Custom" : type, nameof(Type), 16)!,
            Target = Guard.MaxLength(target ?? string.Empty, nameof(Target), 500)!,
            Enabled = enabled,
            IntervalSeconds = Math.Max(0, intervalSeconds),
            ShowInShortcut = true,
            SortOrder = sortOrder,
            Origin = "Agent",
        };

    public void UpdateFromAgent(string displayName, string type, string target, bool enabled, int intervalSeconds)
    {
        DisplayName = Guard.NotEmpty(string.IsNullOrWhiteSpace(displayName) ? Key : displayName, nameof(DisplayName), 200);
        Type = Guard.MaxLength(string.IsNullOrWhiteSpace(type) ? "Custom" : type, nameof(Type), 16)!;
        Target = Guard.MaxLength(target ?? string.Empty, nameof(Target), 500)!;
        Enabled = enabled;
        IntervalSeconds = Math.Max(0, intervalSeconds);
    }
}

/// <summary>The latest status of a monitor point (02 section 6).</summary>
public sealed class MonitorPointState : Entity, ITenantOwned
{
    private MonitorPointState()
    {
        Message = string.Empty;
    }

    public Guid MonitorPointId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid DeviceId { get; private set; }
    public PointStatus Status { get; private set; }
    public string Message { get; private set; }
    public decimal? ResponseMs { get; private set; }
    public DateTimeOffset? LastCheckedAt { get; private set; }
    public DateTimeOffset? StatusSince { get; private set; }

    public static MonitorPointState Create(Guid monitorPointId, Guid tenantId, Guid deviceId) =>
        new() { Id = monitorPointId, MonitorPointId = monitorPointId, TenantId = tenantId, DeviceId = deviceId, Status = PointStatus.Unknown };

    public void Update(PointStatus status, string? message, decimal? responseMs, DateTimeOffset? lastChecked, DateTimeOffset? statusSince)
    {
        Status = status;
        Message = message is null ? string.Empty : message.Length <= 500 ? message : message[..500];
        ResponseMs = responseMs;
        LastCheckedAt = lastChecked;
        StatusSince = statusSince;
    }
}

/// <summary>Tenant monitoring settings (Settings > General): severity and delay of the cloud <c>device-offline</c> alert.</summary>
public sealed class MonitoringSettings : Entity, ITenantOwned
{
    public const int DefaultOfflineDelayMinutes = 2;

    private MonitoringSettings()
    {
    }

    public Guid TenantId { get; private set; }
    public AlertSeverity OfflineSeverity { get; private set; }
    public int OfflineDelayMinutes { get; private set; }

    public static MonitoringSettings Default(Guid tenantId) =>
        new() { Id = tenantId, TenantId = tenantId, OfflineSeverity = AlertSeverity.Critical, OfflineDelayMinutes = DefaultOfflineDelayMinutes };

    public void Update(AlertSeverity offlineSeverity, int offlineDelayMinutes)
    {
        Guard.Against(offlineDelayMinutes is < 1 or > 60, Error.Validation(Guard.ValidationCode, "The offline delay must be 1-60 minutes."));
        OfflineSeverity = offlineSeverity;
        OfflineDelayMinutes = offlineDelayMinutes;
    }
}

/// <summary>A device that went offline: the <c>device-offline</c> alert is due when it is still offline after the delay.</summary>
public sealed class PendingOfflineAlert : Entity, ITenantOwned
{
    private PendingOfflineAlert()
    {
        Reason = string.Empty;
    }

    public Guid DeviceId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid LocationId { get; private set; }
    public string Reason { get; private set; }
    public DateTimeOffset WentOfflineAt { get; private set; }

    public static PendingOfflineAlert Create(Guid deviceId, Guid tenantId, Guid locationId, string reason, DateTimeOffset at) =>
        new() { Id = deviceId, DeviceId = deviceId, TenantId = tenantId, LocationId = locationId, Reason = reason.Length <= 32 ? reason : reason[..32], WentOfflineAt = at };
}
