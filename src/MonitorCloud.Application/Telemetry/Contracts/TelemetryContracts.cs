namespace MonitorCloud.Application.Telemetry.Contracts;

/// <summary>One completed minute from a <c>MetricBatch</c> (05 section 3).</summary>
public sealed record MinuteRow(
    DateTime BucketUtc, short Samples, decimal CpuAvg, decimal CpuMax, decimal CpuP95, decimal RamAvg, decimal RamMax, decimal? DiskActiveAvg,
    long? DiskReadBps, long? DiskWriteBps, decimal? DiskResponseMs, long? NetRxBps, long? NetTxBps, long? NetRxBytes, long? NetTxBytes,
    decimal? PingMs, decimal? PacketLossPercent, decimal? TempMaxC, decimal DiskPercentMax, long UptimeSeconds);

public sealed record DiskRow(string Drive, string? Label, string? FileSystem, decimal TotalGb, decimal UsedGb, decimal FreeGb);

/// <summary>
/// The rows of one guaranteed <c>MetricBatch</c>. <see cref="Committed"/> completes after the batch is durable; only
/// then is the agent acknowledged (05 section 2, rule 5).
/// </summary>
public sealed class TelemetryBatch(Guid deviceId, Guid tenantId, long sequence, IReadOnlyList<MinuteRow> minutes, IReadOnlyList<DiskRow> disks, DateTimeOffset receivedAt)
{
    public Guid DeviceId { get; } = deviceId;
    public Guid TenantId { get; } = tenantId;
    public long Sequence { get; } = sequence;
    public IReadOnlyList<MinuteRow> Minutes { get; } = minutes;
    public IReadOnlyList<DiskRow> Disks { get; } = disks;
    public DateTimeOffset ReceivedAt { get; } = receivedAt;
    public TaskCompletionSource Committed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>The bounded ingestion channel: <see cref="EnqueueAsync"/> waits when it is full (back-pressure, 01 section 8).</summary>
public interface ITelemetryIngest
{
    ValueTask EnqueueAsync(TelemetryBatch batch, CancellationToken cancellationToken);
}

public sealed record StoredSnapshot(byte[] Json, DateTimeOffset CapturedAt);

/// <summary>The latest snapshot per device: in memory, written to <c>telemetry.LiveSnapshots</c> at most once per minute.</summary>
public interface ILiveSnapshotStore
{
    Task SaveAsync(Guid deviceId, Guid tenantId, byte[] json, DateTimeOffset capturedAt, CancellationToken cancellationToken);

    Task<StoredSnapshot?> GetAsync(Guid deviceId, CancellationToken cancellationToken);
}
