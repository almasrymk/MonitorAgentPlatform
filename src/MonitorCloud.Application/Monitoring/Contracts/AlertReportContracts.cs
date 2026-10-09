namespace MonitorCloud.Application.Monitoring.Contracts;

public sealed record AlertReportRow(
    Guid Id, Guid TenantId, Guid LocationId, Guid DeviceId, string Severity, string Category, string Title, string Status, DateTimeOffset FirstSeenAt, DateTimeOffset LastSeenAt,
    DateTimeOffset? ResolvedAt, int Occurrences);

/// <summary>Alerts opened in a period, for reports.</summary>
public interface IAlertReportReader
{
    Task<IReadOnlyList<AlertReportRow>> ListAsync(DateTimeOffset from, DateTimeOffset to, IReadOnlyCollection<Guid>? locationIds, IReadOnlyCollection<Guid>? deviceIds, CancellationToken ct);
}
