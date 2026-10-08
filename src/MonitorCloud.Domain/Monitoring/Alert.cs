using MonitorCloud.SharedKernel;

namespace MonitorCloud.Domain.Monitoring;

public enum AlertSeverity : byte
{
    Info = 1,
    Warning = 2,
    Critical = 3,
}

public enum AlertStatus
{
    Open,
    Resolved,
}

public enum AlertSource
{
    Agent,
    Cloud,
}

public enum ResolvedBy
{
    Auto,
    User,
}

public static class AlertCategories
{
    public const string Performance = "Performance";
    public const string Storage = "Storage";
    public const string Service = "Service";
    public const string Application = "Application";
    public const string Database = "Database";
    public const string Connectivity = "Connectivity";
    public const string System = "System";
    public const string License = "License";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { Performance, Storage, Service, Application, Database, Connectivity, System, License };

    /// <summary>A known category, or System for anything else.</summary>
    public static string Normalize(string? category) =>
        All.FirstOrDefault(c => string.Equals(c, category, StringComparison.OrdinalIgnoreCase)) ?? System;
}

/// <summary>Issue keys raised by the cloud itself (05 section 2, presence; 02 section 6).</summary>
public static class CloudIssues
{
    public const string DeviceOffline = "device-offline";
    public const string License = "license";
    public const string ClockSkew = "clock-skew";
    public const string CloneSuspected = "clone-suspected";

    /// <summary>Alerts that resolve themselves and may not be resolved by a user (<c>ALERT_SELF_RESOLVING</c>).</summary>
    public static bool IsSelfResolving(string issueKey) => issueKey is DeviceOffline or License;
}

public static class MonitoringErrors
{
    public static readonly Error AlertNotFound = Error.NotFound("ALERT_NOT_FOUND", "Alert not found.");
    public static readonly Error SelfResolving = Error.Conflict("ALERT_SELF_RESOLVING", "This alert resolves itself when the condition clears.");
    public static readonly Error AlreadyResolved = Error.Conflict("ALERT_RESOLVED", "The alert is already resolved.");
}

/// <summary>
/// One problem of one device (02 section 6). At most one open alert per (device, issue key): raising it again only
/// touches it (occurrences, last seen, maybe severity).
/// </summary>
public sealed class Alert : AggregateRoot, ILocationScoped
{
    public const int TitleMax = 200;
    public const int MessageMax = 2000;

    private Alert()
    {
        IssueKey = string.Empty;
        Category = AlertCategories.System;
        Title = string.Empty;
        Message = string.Empty;
    }

    public Guid TenantId { get; private set; }
    public Guid LocationId { get; private set; }
    public Guid DeviceId { get; private set; }
    public string IssueKey { get; private set; }
    public string Category { get; private set; }
    public AlertSeverity Severity { get; private set; }
    public string Title { get; private set; }
    public string Message { get; private set; }
    public AlertStatus Status { get; private set; }
    public AlertSource Source { get; private set; }
    public DateTimeOffset FirstSeenAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }
    public int Occurrences { get; private set; }
    public DateTimeOffset? AcknowledgedAt { get; private set; }
    public Guid? AcknowledgedByUserId { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public ResolvedBy? ResolvedBy { get; private set; }

    public bool IsOpen => Status == AlertStatus.Open;

    /// <summary>
    /// A new alert. <paramref name="notify"/> is false for backlog issues older than 15 minutes (05 section 2, rule 7):
    /// they are recorded with their original time but create no notification.
    /// </summary>
    public static Alert Raise(
        Guid tenantId, Guid locationId, Guid deviceId, string issueKey, string? category, AlertSeverity severity, string title, string? message, AlertSource source,
        DateTimeOffset occurredAt, bool notify)
    {
        var alert = new Alert
        {
            TenantId = Guard.NotEmpty(tenantId, nameof(TenantId)),
            LocationId = Guard.NotEmpty(locationId, nameof(LocationId)),
            DeviceId = Guard.NotEmpty(deviceId, nameof(DeviceId)),
            IssueKey = Guard.NotEmpty(issueKey, nameof(IssueKey), 128),
            Category = AlertCategories.Normalize(category),
            Severity = severity,
            Title = Truncate(string.IsNullOrWhiteSpace(title) ? issueKey : title.Trim(), TitleMax),
            Message = Truncate(message?.Trim() ?? string.Empty, MessageMax),
            Status = AlertStatus.Open,
            Source = source,
            FirstSeenAt = occurredAt,
            LastSeenAt = occurredAt,
            Occurrences = 1,
        };
        alert.Raise(new AlertRaisedV1(alert.Id, tenantId, locationId, deviceId, alert.IssueKey, alert.Category, severity.ToString(), alert.Title, notify, occurredAt));
        return alert;
    }

    /// <summary>The condition is reported again: bumps occurrences and last seen; a new severity raises an event.</summary>
    public void Touch(AlertSeverity severity, string? title, string? message, DateTimeOffset at)
    {
        Guard.Against(!IsOpen, MonitoringErrors.AlreadyResolved);
        Occurrences++;
        if (at > LastSeenAt)
            LastSeenAt = at;
        if (!string.IsNullOrWhiteSpace(title))
            Title = Truncate(title.Trim(), TitleMax);
        if (message is not null)
            Message = Truncate(message.Trim(), MessageMax);
        if (severity != Severity)
        {
            var previous = Severity;
            Severity = severity;
            Raise(new AlertSeverityChangedV1(Id, TenantId, LocationId, DeviceId, previous.ToString(), severity.ToString(), at));
        }
    }

    public void Acknowledge(Guid userId, DateTimeOffset at)
    {
        Guard.Against(!IsOpen, MonitoringErrors.AlreadyResolved);
        AcknowledgedAt ??= at;
        AcknowledgedByUserId ??= userId;
    }

    public void Resolve(ResolvedBy by, DateTimeOffset at)
    {
        if (!IsOpen)
            return;
        Guard.Against(by == Monitoring.ResolvedBy.User && CloudIssues.IsSelfResolving(IssueKey), MonitoringErrors.SelfResolving);
        Status = AlertStatus.Resolved;
        ResolvedAt = at;
        ResolvedBy = by;
        Raise(new AlertResolvedV1(Id, TenantId, LocationId, DeviceId, IssueKey, Severity.ToString(), by.ToString(), at));
    }

    /// <summary>The device moved: its open alerts follow it.</summary>
    public void MoveTo(Guid locationId) => LocationId = Guard.NotEmpty(locationId, nameof(LocationId));

    /// <summary>Seeding: an alert opened and resolved in the past.</summary>
    public void SeedResolved(DateTimeOffset at)
    {
        Status = AlertStatus.Resolved;
        ResolvedAt = at;
        ResolvedBy = Monitoring.ResolvedBy.Auto;
        LastSeenAt = at;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}

/// <summary>Alert events carry names (not the enums) so other modules can read them (module isolation).</summary>
public sealed record AlertRaisedV1(Guid AlertId, Guid TenantId, Guid LocationId, Guid DeviceId, string IssueKey, string Category, string Severity, string Title, bool Notify, DateTimeOffset At) : DomainEvent(At);

public sealed record AlertSeverityChangedV1(Guid AlertId, Guid TenantId, Guid LocationId, Guid DeviceId, string From, string To, DateTimeOffset At) : DomainEvent(At);

public sealed record AlertResolvedV1(Guid AlertId, Guid TenantId, Guid LocationId, Guid DeviceId, string IssueKey, string Severity, string By, DateTimeOffset At) : DomainEvent(At);
