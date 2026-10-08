namespace MonitorCloud.Application.Monitoring;

public sealed record AlertDto(
    Guid Id, Guid DeviceId, string? DeviceName, Guid LocationId, string? LocationName, string IssueKey, string Category, string Severity, string Title, string Message,
    string Status, string Source, DateTimeOffset FirstSeenAt, DateTimeOffset LastSeenAt, int Occurrences, DateTimeOffset? AcknowledgedAt, DateTimeOffset? ResolvedAt,
    string? ResolvedBy, bool CanResolve);

public sealed record PlatformAlertDto(
    Guid Id, Guid TenantId, string? CustomerName, Guid DeviceId, string? DeviceName, Guid LocationId, string? LocationName, string Category, string Severity, string Title,
    string Message, string Status, DateTimeOffset FirstSeenAt, DateTimeOffset LastSeenAt, int Occurrences);

public sealed record MonitorPointDto(
    Guid Id, string Key, string DisplayName, string Type, string Target, bool Enabled, bool ShowInShortcut, int IntervalSeconds, string Status, string Message,
    decimal? ResponseMs, DateTimeOffset? LastCheckedAt, DateTimeOffset? StatusSince);
