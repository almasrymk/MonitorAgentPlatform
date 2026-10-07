using Microsoft.Extensions.Logging;
using MonitorCloud.AgentGateway.Sessions;
using MonitorCloud.AgentProtocol.V1;
using MonitorCloud.Application.Telemetry.Contracts;

namespace MonitorCloud.AgentGateway.Handlers;

/// <summary>
/// <c>MetricBatch</c> -> the ingestion channel (05 section 3). The read loop only waits when the channel is full
/// (back-pressure); the acknowledgement is sent when the writer has committed the batch.
/// </summary>
public sealed partial class MetricBatchHandler(ITelemetryIngest ingest, TimeProvider clock, ILogger<MetricBatchHandler> logger) : IAgentMessageHandler
{
    public const int MaxMinutesPerBatch = 24 * 60;

    public AgentMessage.BodyOneofCase Kind => AgentMessage.BodyOneofCase.MetricBatch;

    public async Task HandleAsync(AgentSession session, AgentMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(message);
        if (!await GuaranteedMessages.AcceptAsync(session, message, clock, cancellationToken))
            return;
        if (session.Restricted)
        {
            // Unlicensed after the grace period: acknowledged and dropped (05 section 2, rule 9).
            await GuaranteedMessages.AckAsync(session, message, clock, cancellationToken);
            return;
        }

        var batch = message.MetricBatch;
        var minutes = batch.Minutes.Take(MaxMinutesPerBatch).Where(m => m.BucketStart is not null).Select(ToRow).ToList();
        var disks = batch.Disks.Where(d => !string.IsNullOrWhiteSpace(d.Drive))
            .Select(d => new DiskRow(d.Drive, Empty(d.Label), Empty(d.FileSystem), Gb(d.TotalGb), Gb(d.UsedGb), Gb(d.FreeGb))).ToList();
        var work = new TelemetryBatch(session.DeviceId, session.TenantId, (long)message.Sequence, minutes, disks, clock.GetUtcNow());
        await ingest.EnqueueAsync(work, cancellationToken);
        _ = AcknowledgeWhenCommittedAsync(session, message, work);
    }

    private async Task AcknowledgeWhenCommittedAsync(AgentSession session, AgentMessage message, TelemetryBatch work)
    {
        try
        {
            await work.Committed.Task.WaitAsync(session.Closed);
            await GuaranteedMessages.AckAsync(session, message, clock, session.Closed);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or InvalidOperationException or Grpc.Core.RpcException)
        {
            // The stream ended first: the agent resends after its next Welcome.
            LogNotAcknowledged(logger, session.DeviceId, message.Sequence);
        }
    }

    internal static MinuteRow ToRow(MinuteAggregate m)
    {
        var bucket = m.BucketStart.ToDateTime();
        bucket = new DateTime(bucket.Year, bucket.Month, bucket.Day, bucket.Hour, bucket.Minute, 0, DateTimeKind.Utc);
        return new MinuteRow(
            bucket, (short)Math.Clamp(m.Samples, 0, short.MaxValue), Percent(m.CpuAvg), Percent(m.CpuMax), Percent(m.CpuP95), Percent(m.RamAvg), Percent(m.RamMax),
            m.HasDiskActiveAvg ? Percent(m.DiskActiveAvg) : null, m.HasDiskReadBps ? Bps(m.DiskReadBps) : null, m.HasDiskWriteBps ? Bps(m.DiskWriteBps) : null,
            m.HasDiskResponseMs ? Ms(m.DiskResponseMs) : null, m.HasNetRxBps ? Bps(m.NetRxBps) : null, m.HasNetTxBps ? Bps(m.NetTxBps) : null,
            m.HasNetRxBytes ? Bps(m.NetRxBytes) : null, m.HasNetTxBytes ? Bps(m.NetTxBytes) : null, m.HasPingMs ? Ms(m.PingMs) : null,
            m.HasPacketLossPercent ? Percent(m.PacketLossPercent) : null, m.HasTempMaxC ? (decimal)Math.Round(Math.Clamp(m.TempMaxC, -99, 999), 1) : null,
            Percent(m.DiskPercentMax), Math.Max(0, m.UptimeSeconds));
    }

    private static decimal Percent(double value) => (decimal)Math.Round(Math.Clamp(double.IsFinite(value) ? value : 0, 0, 100), 2);

    private static decimal Ms(double value) => (decimal)Math.Round(Math.Clamp(double.IsFinite(value) ? value : 0, 0, 9_999_999), 2);

    private static long Bps(ulong value) => (long)Math.Min(value, long.MaxValue);

    private static decimal Gb(double value) => (decimal)Math.Round(Math.Clamp(double.IsFinite(value) ? value : 0, 0, 9_999_999_999), 2);

    private static string? Empty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    [LoggerMessage(Level = LogLevel.Debug, Message = "Batch {Sequence} of device {DeviceId} committed after the stream closed; not acknowledged")]
    private static partial void LogNotAcknowledged(ILogger logger, Guid deviceId, ulong sequence);
}
