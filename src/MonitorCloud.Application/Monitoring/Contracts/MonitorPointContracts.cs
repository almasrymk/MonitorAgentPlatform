namespace MonitorCloud.Application.Monitoring.Contracts;

public sealed record MonitorPointDefinition(
    string Key, string DisplayName, string Type, string Target, int IntervalSeconds, string AlertLevel, bool Enabled, bool ShowInShortcut, string? SettingsJson);

/// <summary>The monitor point definitions of a device, for the configuration document (Configuration module).</summary>
public interface IMonitorPointCatalog
{
    Task<IReadOnlyList<MonitorPointDefinition>> GetAsync(Guid deviceId, CancellationToken ct);
}
