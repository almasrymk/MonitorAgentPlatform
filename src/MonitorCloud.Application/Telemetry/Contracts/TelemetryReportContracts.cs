namespace MonitorCloud.Application.Telemetry.Contracts;

/// <summary>Usage of one device over a period, from <c>MetricHours</c> (weighted by samples).</summary>
public sealed record DeviceUsage(Guid DeviceId, int Hours, decimal? CpuAvg, decimal? CpuMax, decimal? RamAvg, decimal? RamMax, decimal? DiskMax, long RxBytes, long TxBytes);

public interface ITelemetryReportReader
{
    Task<IReadOnlyDictionary<Guid, DeviceUsage>> UsageAsync(IReadOnlyCollection<Guid> deviceIds, DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
