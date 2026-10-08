using MonitorCloud.SharedKernel;

namespace MonitorCloud.Domain.Telemetry;

/// <summary>
/// The measured values of one bucket (02 section 5). Rows are written with SqlBulkCopy by the telemetry writer and
/// read with EF Core (tenant filters apply); they have no behaviour.
/// </summary>
public abstract class MetricBucket : Entity, ITenantOwned
{
    public Guid DeviceId { get; protected set; }
    public Guid TenantId { get; protected set; }

    /// <summary>Start of the minute (or hour), UTC.</summary>
    public DateTime BucketUtc { get; protected set; }

    public decimal CpuAvg { get; protected set; }
    public decimal CpuMax { get; protected set; }
    public decimal CpuP95 { get; protected set; }
    public decimal RamAvg { get; protected set; }
    public decimal RamMax { get; protected set; }
    public decimal? DiskActiveAvg { get; protected set; }
    public long? DiskReadBps { get; protected set; }
    public long? DiskWriteBps { get; protected set; }
    public decimal? DiskResponseMs { get; protected set; }
    public long? NetRxBps { get; protected set; }
    public long? NetTxBps { get; protected set; }
    public long? NetRxBytes { get; protected set; }
    public long? NetTxBytes { get; protected set; }
    public decimal? PingMs { get; protected set; }
    public decimal? PacketLossPercent { get; protected set; }
    public decimal? TempMaxC { get; protected set; }
    public decimal DiskPercentMax { get; protected set; }
    public short Samples { get; protected set; }
}

/// <summary><c>telemetry.MetricMinutes</c>, PK (DeviceId, BucketUtc).</summary>
public sealed class MetricMinute : MetricBucket
{
    private MetricMinute()
    {
    }
}

/// <summary><c>telemetry.MetricHours</c>: minutes aggregated by <c>MetricRollupJob</c>.</summary>
public sealed class MetricHour : MetricBucket
{
    private MetricHour()
    {
    }
}

/// <summary><c>telemetry.DiskUsageHours</c>, PK (DeviceId, Drive, BucketUtc): the last reported size of each partition per hour.</summary>
public sealed class DiskUsageHour : Entity, ITenantOwned
{
    private DiskUsageHour()
    {
        Drive = string.Empty;
    }

    public Guid DeviceId { get; private set; }
    public Guid TenantId { get; private set; }
    public string Drive { get; private set; }
    public DateTime BucketUtc { get; private set; }
    public string? Label { get; private set; }
    public string? FileSystem { get; private set; }
    public decimal TotalGb { get; private set; }
    public decimal UsedGb { get; private set; }
    public decimal FreeGb { get; private set; }
}

/// <summary><c>telemetry.LiveSnapshots</c>: the latest full snapshot of a device (Brotli JSON, 05 section 5).</summary>
public sealed class LiveSnapshot : Entity, ITenantOwned
{
    private LiveSnapshot()
    {
        Json = [];
    }

    public Guid DeviceId { get; private set; }
    public Guid TenantId { get; private set; }
    public byte[] Json { get; private set; }
    public DateTimeOffset CapturedAt { get; private set; }
}

/// <summary><c>telemetry.MonitorPointSamples</c>: the status of a monitor point once a minute (02 section 5).</summary>
public sealed class MonitorPointSample : Entity, ITenantOwned
{
    private MonitorPointSample()
    {
    }

    public Guid MonitorPointId { get; private set; }
    public DateTime BucketUtc { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid DeviceId { get; private set; }
    public byte Status { get; private set; }
    public decimal? ResponseMs { get; private set; }
}
