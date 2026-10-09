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

    /// <summary>Type-specific settings for the agent (no secrets; 02 section 6).</summary>
    public string? SettingsJson { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public const string AgentOrigin = "Agent";
    public const string CloudOrigin = "Cloud";

    public static readonly IReadOnlyList<string> Types = ["Website", "Database", "Ping", "Application", "Service", "Disk", "Network", "Custom"];
    public static readonly IReadOnlyList<string> AlertLevels = ["Problem", "Warning", "Unknown"];

    /// <summary>A point created in the portal (M8): the cloud owns it.</summary>
    public static MonitorPoint FromCloud(
        Guid tenantId, Guid deviceId, string key, string displayName, string type, string target, int intervalSeconds, string alertLevel, bool enabled, bool showInShortcut,
        string? settingsJson, int sortOrder)
    {
        var point = new MonitorPoint
        {
            TenantId = Guard.NotEmpty(tenantId, nameof(TenantId)),
            DeviceId = Guard.NotEmpty(deviceId, nameof(DeviceId)),
            Key = Guard.NotEmpty(key, nameof(Key), 64),
            Origin = CloudOrigin,
            SortOrder = sortOrder,
        };
        point.Edit(displayName, type, target, intervalSeconds, alertLevel, enabled, showInShortcut, settingsJson);
        return point;
    }

    /// <summary>Edited in the portal: the definition is validated per type and the cloud owns the point from now on.</summary>
    public void Edit(string displayName, string type, string target, int intervalSeconds, string alertLevel, bool enabled, bool showInShortcut, string? settingsJson)
    {
        Guard.Against(!Types.Contains(type), Error.Validation(Guard.ValidationCode, $"type must be one of {string.Join(", ", Types)}."));
        Guard.Against(!AlertLevels.Contains(alertLevel), Error.Validation(Guard.ValidationCode, "alertLevel must be Problem, Warning or Unknown."));
        DisplayName = Guard.NotEmpty(displayName, nameof(DisplayName), 200);
        Type = type;
        Target = MonitorPointRules.Target(type, target);
        IntervalSeconds = Guard.InRange(intervalSeconds, nameof(IntervalSeconds), 5, 86_400);
        AlertLevel = alertLevel;
        Enabled = enabled;
        ShowInShortcut = showInShortcut;
        SettingsJson = Guard.MaxLength(settingsJson, nameof(SettingsJson), 8_000);
        Origin = CloudOrigin;
    }

    /// <summary>The cloud takes over a point the agent reported (the device is managed from the portal).</summary>
    public void Manage() => Origin = CloudOrigin;

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

/// <summary>Validation of a monitor point target per type (09 section 2).</summary>
public static class MonitorPointRules
{
    public static string Target(string type, string? target)
    {
        var value = Guard.NotEmpty(target, "Target", 500);
        switch (type)
        {
            case "Website":
                Guard.Against(!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps),
                    Error.Validation(Guard.ValidationCode, "A website target must be an absolute http or https address."));
                break;
            case "Ping":
            case "Network":
                Guard.Against(value.Contains(' ', StringComparison.Ordinal) || value.Contains('/', StringComparison.Ordinal),
                    Error.Validation(Guard.ValidationCode, "A ping target must be a host name or an IP address."));
                break;
            case "Disk":
                Guard.Against(value.Length > 64, Error.Validation(Guard.ValidationCode, "A disk target is a drive or a mount point."));
                break;
        }

        return value;
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

    /// <summary>The settings of a tenant that never saved its own: Critical after the platform default delay.</summary>
    public static MonitoringSettings Default(Guid tenantId, int offlineDelayMinutes = DefaultOfflineDelayMinutes) =>
        new() { Id = tenantId, TenantId = tenantId, OfflineSeverity = AlertSeverity.Critical, OfflineDelayMinutes = offlineDelayMinutes is >= 1 and <= 60 ? offlineDelayMinutes : DefaultOfflineDelayMinutes };

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
